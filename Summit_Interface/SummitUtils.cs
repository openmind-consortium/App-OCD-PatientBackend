///-------------------------------------------------------------------------------------------------
// file:	SummitUtils.cs
//
// summary:	Class which holds all of the helper/utility functions for the main program. Does most of the heaving lifting
// 
// David Xing 6/20/2018
///-------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using Medtronic.SummitAPI.Classes;
using Medtronic.SummitAPI.Events;
using Medtronic.TelemetryM;
using Medtronic.TelemetryM.CtmProtocol.Commands;
using Medtronic.NeuroStim.Olympus.DataTypes.Core;
using Medtronic.NeuroStim.Olympus.DataTypes.Sensing;
using Medtronic.NeuroStim.Olympus.DataTypes.Measurement;
using Medtronic.NeuroStim.Olympus.Commands;
using Medtronic.NeuroStim.Olympus.DataTypes.PowerManagement;
using Medtronic.NeuroStim.Olympus.DataTypes.Therapy;
using Medtronic.NeuroStim.Olympus.DataTypes.DeviceManagement;


namespace Summit_Interface
{
    ///-------------------------------------------------------------------------------------------------
    /// <summary>   Collection of helper functions for interfacing with Summit API. </summary>
    ///-------------------------------------------------------------------------------------------------
    public static class SummitUtils
    {

        /// <summary>   Struct holding all the parameters to set up FFT streaming. </summary>
        /// 
        /// <remarks>   <see cref="GetFFTParameters(INSParameters, out FftParameters)"/> initiates the values from the JSON parameters. 
        ///             Used in <see cref="ConfigureFFT(INSParameters, out FftConfiguration, out SenseTimeDomainChannel)"/>. </remarks>
        public struct FftParameters
        {
            /// <summary>   Which channel to stream from. </summary>
            public SenseTimeDomainChannel channel;
            /// <summary>   FFT Size. </summary>
            public FftSizes size;
            /// <summary>   FFT interval. </summary>
            public int interval;
            /// <summary>   True to enable windowing, false to disable. </summary>
            public bool windowEnabled;
            /// <summary>   The window load. </summary>
            public FftWindowAutoLoads windowLoad;
            /// <summary>   Size of the FFT bins. </summary>
            public int binSize;
            /// <summary>   The bin offset. </summary>
            public int binOffset;
        }

        /// <summary>   Struct holding all the parameters to set up a stimulation group (i.e. the parameters in <see cref="TherapyGroup"/>. </summary>
        /// 
        /// <remarks>   <see cref="GetGroupParameters(INSParameters, string, int?, out StimGroupParameters)"/> initiates the values from the JSON parameters. 
        ///             Used in <see cref="ConfigureStimGroup(INSParameters, string, int?, out TherapyGroup, out GroupNumber, out ActiveGroup)"/>. </remarks>
        public struct StimGroupParameters
        {
            /// <summary>   The group number. </summary>
            public GroupNumber groupNum;
            /// <summary>   The group active (same as group number). </summary>
            public ActiveGroup groupNumActive;
            /// <summary>   Type of the ramp. </summary>
            public RampingTypes rampType;
            /// <summary>   The ramp time. </summary>
            public int rampTime;
            /// <summary>   The pulse width lower limit in 10's of microseconds. </summary>
            public int pwLower;
            /// <summary>   The pulse width upper limit in 10's of microseconds. </summary>
            public int pwUpper;
            /// <summary>   The rate period of the group in 10's of microseconds. </summary>
            public int ratePeriod;
            /// <summary>   The upper limit of the rate period in 10's of microseconds. </summary>
            public int ratePeriodUpper;
            /// <summary>   The lower limit of the rate period in 10's of microseconds. </summary>
            public int ratePeriodLower;
        }

        /// <summary>   Struct holding the parameters to set up a stimulation program (i.e. the parameters in <see cref="TherapyProgram"/>. </summary>
        /// 
        /// <remarks>   <see cref="GetProgramParameters(INSParameters, string, int?, out StimProgramParameters)"/> initiates the values from JSON parameters. 
        ///             Used in <see cref="ConfigureStimProgram(INSParameters, string, int?, out TherapyProgram, out int, out int, StimProgramParameters?)"/>. </remarks>
        public struct StimProgramParameters
        {
            /// <summary>   The anode. </summary>
            public int anode;
            /// <summary>   The cathode. </summary>
            public int cathode;
            /// <summary>   The pulse width in 10's of microseconds. </summary>
            public int pulseWidth;
            /// <summary>   The amplitude in 0.1's of milliamps. </summary>
            public int amplitude;
            /// <summary>   The amplitude lower limit in 0.1's of milliamps. </summary>
            public int ampLower;
            /// <summary>   The amplitude lower limit in 0.1's of milliamps. </summary>
            public int ampUpper;
        }

        /// <summary>   A struct holding the configuration parameters for sweeping (Single Pulse sweeping or Stim Burst sweeping). </summary>
        /// 
        /// <remarks>   <see cref="configureStimSweep(INSParameters, SummitSystem, string, out StimSweeper, ref TherapyGroup, ref StimProgramParameters, ref SweepConfigParameters)"/> initializes the values after some processing of the JSON parameters. 
        ///             Used in the main function during stim-sweeping setup to configure group and program parameters based on the desired sweep values. </remarks> 
        public struct SweepConfigParameters
        {
            /// <summary>   The rate period starting value in 10's of microseconds. </summary>
            public ushort? startingRatePeriod;
            /// <summary>   The rate period upper limit in 10's of microseconds. </summary>
            public ushort ratePeriodUpperLimit;
            /// <summary>   The rate period lower limit in 10's of microseconds. </summary>
            public ushort ratePeriodLowerLimit;
            /// <summary>   The amplitude starting value in 0.1's of milliamps. </summary>
            public int startingAmp;
            /// <summary>   The amplitude lower limit in 0.1's of milliamps. </summary>
            public int ampLowerLimit;
            /// <summary>   The amplitude upper limit in 0.1's of milliamps. </summary>
            public int ampUpperLimit;
            /// <summary>   The pulse width starting value in 10's of microseconds. </summary>
            public int startingPulseWidth;
            /// <summary>   The pulse width lower limit in 10's of microseconds. </summary>
            public int pulseWidthLowerLimit;
            /// <summary>   The pulse width upper limit in 10's of microseconds. </summary>
            public int pulseWidthUpperLimit;
            /// <summary>   The group number where the stim sweep is running on. </summary>
            public GroupNumber stimSweepGroupNum;
            /// <summary>   The stim sweep group active (same as group number). </summary>
            public ActiveGroup stimSweepGroupActive;
        }

        /// <summary>   A struct holding variables used for interpolating missing data due to dropped packets of time-domain data. </summary>
        /// 
        /// <remarks>   <see cref="SummitProgram.SummitTimeDomainPacketReceived(object, SensingEventTD)"/> fills the struct with the correct values, then
        ///             passes it to <see cref="InterpolateDroppedSamples(INSBuffer, INSBuffer, SensingEventTD, InterpolationParameters)"/> to do the interpolation. </remarks>
        public struct InterpolationParameters
        {
            /// <summary>   The number of time-domain channels. </summary>
            public int nChans;
            /// <summary>   The difference in timestamp bewteen the current and previous packet. </summary>
            public int timestampDiff;
            /// <summary>   The conversion rate to turn the time in seconds to the timestamp value (in timestampDiff). </summary>
            public double secondsToTimeStamp;
            /// <summary>   The number of dropped packets. </summary>
            public int nDroppedPackets;
            /// <summary>   The sampling rate. </summary>
            public TdSampleRates samplingRate;
            /// <summary>   The number of data samples in the previous received packet. </summary>
            public int prevPacketNSamples;
            /// <summary>   The packet number of the previous received packet. </summary>
            public int prevPacketNum;
            /// <summary>   The INS timestamp of the previous received packet (uint16). </summary>
            public ushort prevPacketTime;
            /// <summary>   The timestamp estimate of the previous received packet (int64). </summary>
            public long prevPacketEstTimeDiff;
            /// <summary>   The values of the most recent data point in the previous received packet. </summary>
            public double[] prevValues;
        }

        /// <summary>   Struct holding all the parameters about sensing. </summary>
        /// 
        /// <remarks>   Used to get the senseing setup information from the INS. </remarks>
        public struct SenseParameters
        {
            /// <summary>   The number of time-domain channels. </summary>
            public int nChans;
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Reset POR if turning Therapy on failed. </summary>
        /// 
        /// <remarks>   Straight up taken from the Medtronic training code. </remarks>
        ///
        /// <param name="theSummit">    the SummitSystem object. </param>
        ///
        /// <returns>   APIReturnInfo. </returns>
        ///-------------------------------------------------------------------------------------------------
        public static APIReturnInfo resetPOR(SummitSystem theSummit)
        {
            Console.WriteLine("POR was set, resetting...");

            // reset POR
            theSummit.ResetErrorFlags(Medtronic.NeuroStim.Olympus.DataTypes.Core.StatusBits.Por);

            // check battery
            BatteryStatusResult theStatus;
            theSummit.ReadBatteryLevel(out theStatus);

            // perform interrogate command and check if therapy is enabled.s
            GeneralInterrogateData interrogateBuffer;
            APIReturnInfo theInfo = theSummit.ReadGeneralInfo(out interrogateBuffer);
            if (interrogateBuffer.IsTherapyUnavailable)
            {
                Console.WriteLine("Therapy still unavailable after reset");
            }

            return theInfo;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Function to connect to the Summit System. </summary>
        /// 
        /// <remarks>   Straight-up taken from the Medtronic training code</remarks>
        ///
        /// <param name="theSummitManager"> The summit manager object. </param>
        /// <param name="theSummit">        [in,out] the summit system object. </param>
        ///
        /// <returns>   True if it succeeds, false if it fails. </returns>
        ///-------------------------------------------------------------------------------------------------
        public static bool SummitConnect(SummitManager theSummitManager, ref SummitSystem theSummit, ref SummitSystemWrapper summitWrapper, ushort teleMode)
        {
            // Bond with any CTMs plugged in over USB
            Console.WriteLine("Checking USB for unbonded CTMs. Please make sure they are powered on.");
            theSummitManager.GetUsbTelemetry();

            // Look for known telemetry
            List<InstrumentInfo> knownTelemetry = theSummitManager.GetKnownTelemetry();

            // Check for empty list, look for USB CTMS
            if (knownTelemetry.Count == 0)
            {
                do
                {
                    // Inform user we will loop until a CTM is found on USBs
                    Console.WriteLine("SummitConnect: No CTMs found, retrying on USB...");
                    Thread.Sleep(2000);
                    // No previously paired CTMs found, look for CTMs on USB
                    knownTelemetry = theSummitManager.GetUsbTelemetry();
                } while (knownTelemetry.Count == 0);
            }

            // Write out the known instruments
            Console.WriteLine("SummitConnect: Bonded Instruments Found:");
            foreach (InstrumentInfo inst in knownTelemetry)
            {
                Console.WriteLine(inst.SerialNumber);
            }

            // Connect to the first CTM available, then try others if it fails
            SummitSystem tempSummit = null;
            for (int i = 0; i < theSummitManager.GetKnownTelemetry().Count; i++)
            {
                ManagerConnectStatus connectReturn = theSummitManager.CreateSummit(out theSummit, theSummitManager.GetKnownTelemetry()[i], telemetryMode: teleMode,
                    ctmBeepEnables: (CtmBeepEnables.NoDeviceDiscovered | CtmBeepEnables.GeneralAlert | CtmBeepEnables.TelMLost));

                // Write out the result
                Console.WriteLine("Create Summit Result: " + connectReturn.ToString());

                // Break if successful
                if (connectReturn == ManagerConnectStatus.Success)
                {
                    //add summit system to wrapper
                    summitWrapper.setSummit(ref theSummit);
                    break;
                }
            }

            // Make sure telemetry was connected to, if not fail
            if (theSummit == null)
            {
                // inform user that CTM was not successfully connected to
                Console.WriteLine("SummitConnect: Failed to connect to CTM, returning false...");
                return false;
            }
            else
            {
                // inform user that CTM was successfully connected to
                Console.WriteLine("CTM Connection Successful!");

                // Discovery INS with the connected CTM, loop until a device has been discovered
                List<DiscoveredDevice> discoveredDevices;
                do
                {
                    theSummit.OlympusDiscovery(out discoveredDevices);
                } while (discoveredDevices.Count == 0);

                // Report Discovery Results to User
                Console.WriteLine("Olympi found:");
                foreach (DiscoveredDevice ins in discoveredDevices)
                {
                    Console.WriteLine(ins);
                }

                // Connect to the INS with default parameters and ORCA annotations
                Console.WriteLine("Creating Summit Interface.");

                // We can disable ORCA annotations because this is a non-human use INS (see disclaimer)
                // Human-use INS devices ignore the OlympusConnect disableAnnotation flag and always enable annotations.
                // Connect to a device
                ConnectReturn theWarnings;
                APIReturnInfo connectReturn;
                int i = 0;
                do
                {
                    connectReturn = theSummit.StartInsSession(discoveredDevices[0], out theWarnings, true);
                    i++;
                } while (theWarnings.HasFlag(ConnectReturn.InitializationError));

                // Write out the number of times a StartInsSession was attempted with initialization errors
                Console.WriteLine("Initialization Error Count: " + i.ToString());

                // Write out the final result of the example
                if (connectReturn.RejectCode != 0)
                {
                    Console.WriteLine("Summit Initialization: INS failed to connect");
                    theSummitManager.DisposeSummit(theSummit);
                    return false;
                }
                else
                {
                    // Write out the warnings if they exist
                    Console.WriteLine("Summit Initialization: INS connected, warnings: " + theWarnings.ToString());
                    //theSummit = tempSummit;

                    APIReturnInfo success = theSummit.WriteTelemetryParameters(4, 4);
                    return true;
                }

            }

        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Generates time domain channel configurations from the JSON parameters.
        ///             </summary>
        /// 
        /// <remarks>   Uses <see cref="GetSenseParameters(List{int}, List{int}, List{int}, List{int}, List{double}, int, out TdMuxInputs, out TdMuxInputs, out TdLpfStage1, out TdLpfStage2, out TdHpfs, ref TdSampleRates)    
        /// "/> to extract the paraters from the JSON file. Configures a list of up to 4 <see cref="TimeDomainChannel"/>s as output. Performs some error checking such as:
        /// <list type="bullet">
        /// <item>
        /// <description>Ensuring .</description>
        /// </item>
        /// <item>
        /// <description>Item 2.</description>
        /// </item>
        /// </list>
        /// </remarks>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="parameters">           The <see cref="INSParameters"/> object which holds all the parameters loaded from the JSON file </param>
        /// <param name="indexInJSON">          [out] A list of which index in the JSON array each timeDomainChannel in the <paramref name="timeDomainChannels"/>. </param>
        /// <param name="timeDomainChannels">   [out] A list of up to 4 <see cref="TimeDomainChannel"/> objects configured according to the JSON parameters. </param>
        /// <param name="samplingRate">         [in,out] The sampling rate enum. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void ConfigureTimeDomain(INSParameters parameters, out List<int?> indexInJSON, out List<TimeDomainChannel> timeDomainChannels,
            ref TdSampleRates samplingRate)
        {
            //figure out which electrodes should be bore 1 and bore 2
            var allAnodes = parameters.GetParam("Sense.Anode", typeof(int));
            var allCathodes = parameters.GetParam("Sense.Cathode", typeof(int));
            var allStage1LPFs = parameters.GetParam("Sense.LowPassCutoffStage1", typeof(int));
            var allStage2LPFs = parameters.GetParam("Sense.LowPassCutoffStage2", typeof(int));
            var allHPFs = parameters.GetParam("Sense.HighPassCutoff", typeof(double));

            //first check that channel counts are consistent (should never happen if JSON error checking specifications are correctly implemented)
            if (allAnodes.Count != allCathodes.Count || allCathodes.Count != allStage1LPFs.Count ||
                allStage1LPFs.Count != allStage2LPFs.Count || allStage2LPFs.Count != allHPFs.Count)
            {
                throw new Exception(String.Format("Sensing channel specifications counts aren't consistent! \n" +
                    "# anodes: {0}, # cathodes: {1}, # Stage1 LPFs: {2}, # Stage2 LPFs: {3}, # HPFs: {4} \n" +
                    "Tell David to check JSON definitions!",
                    allAnodes.Count, allCathodes.Count, allStage1LPFs.Count, allStage2LPFs.Count, allHPFs.Count));
            }


            // organize all parameters into bores
            List<int> bore1Anodes = new List<int>();
            List<int> bore1Cathodes = new List<int>();
            List<int> bore1Stage1LPFs = new List<int>();
            List<int> bore1Stage2LPFs = new List<int>();
            List<double> bore1HPFs = new List<double>();

            List<int> bore2Anodes = new List<int>();
            List<int> bore2Cathodes = new List<int>();
            List<int> bore2Stage1LPFs = new List<int>();
            List<int> bore2Stage2LPFs = new List<int>();
            List<double> bore2HPFs = new List<double>();

            // also track the ordering of the channels for when we define power bands later
            indexInJSON = new List<int?> { null, null, null, null };
            int boreNum;

            for (int iChan = 0; iChan < allAnodes.Count; iChan++)
            {

                //check that the anodes and cathoes do not cross bores
                if ((allAnodes[iChan] <= 7 && allCathodes[iChan] > 7) || (allAnodes[iChan] > 7 && allCathodes[iChan] <= 7))
                {
                    if (allAnodes[iChan] != 16 && allCathodes[iChan] != 16) //don't need to check if one of the electrodes is floating
                    {
                        throw new Exception(String.Format("Cannot sense across bores! Anode was set as {0}, cathode as {1}",
                            allAnodes[iChan].ToString(), allCathodes[iChan].ToString()));
                    }
                }

                //ok, parse into bore 1 or bore 2
                if (allAnodes[iChan] == 16)
                {
                    //cathode will tell us the bore if the anode is floating
                    if (allCathodes[iChan]==16)
                    {
                        throw new Exception("Can't have both anode and cathode as floating!");

                    }
                    else if (allCathodes[iChan] <= 7)
                    {
                        boreNum = 1;
                    }
                    else
                    {
                        boreNum = 2;
                    }

                }
                else if (allAnodes[iChan] <= 7)
                {
                    boreNum = 1;
                }
                else
                {
                    boreNum = 2;
                }

                //add to the correct bore
                if (boreNum == 1)
                {
                    bore1Anodes.Add(allAnodes[iChan]);
                    bore1Cathodes.Add(allCathodes[iChan]);
                    bore1Stage1LPFs.Add(allStage1LPFs[iChan]);
                    bore1Stage2LPFs.Add(allStage2LPFs[iChan]);
                    bore1HPFs.Add(allHPFs[iChan]);

                    //also store which index in the JSON array it was into the channel list
                    indexInJSON[bore1Anodes.Count - 1] = iChan;
                }
                else
                {
                    bore2Anodes.Add(allAnodes[iChan] - 8);
                    bore2Cathodes.Add(allCathodes[iChan] - 8);
                    bore2Stage1LPFs.Add(allStage1LPFs[iChan]);
                    bore2Stage2LPFs.Add(allStage2LPFs[iChan]);
                    bore2HPFs.Add(allHPFs[iChan]);

                    //also store which index in the JSON array it was into the channel list
                    indexInJSON[bore2Anodes.Count - 1 + 2] = iChan;
                }
            }

            //finally check bore counts, can't have more than 2 per bore
            if (bore1Anodes.Count > 2 || bore2Anodes.Count > 2)
            {
                throw new Exception(String.Format("Cannot have more than 2 channels per bore! # chans in bore 1: {0}, # chans in bore 2: {1}",
                    bore1Anodes.Count, bore2Anodes.Count));
            }

            //parse sampling rates
            int paramSamplingRate = parameters.GetParam("Sense.SamplingRate", typeof(int));
            try
            {
                samplingRate = (TdSampleRates)Enum.Parse(typeof(TdSampleRates), "Sample" + paramSamplingRate.ToString("0000") + "Hz");
            }
            catch
            {
                throw new Exception(String.Format("Chosen sampling rate: {0}, isn't a valid selection, check JSON file specifications!",
                   paramSamplingRate));
            }

            //sampling at 1000Hz limits sensing to 2 channels due to streaming bandwidth limitations
            if (samplingRate == TdSampleRates.Sample1000Hz && (bore1Anodes.Count + bore2Anodes.Count) > 2)
            {
                throw new Exception(String.Format("For 1000Hz sampling rate, can only have two sense channels! Currently has {0} channels",
                    bore1Anodes.Count + bore2Anodes.Count));
            }

            // Create a sensing configuration
            timeDomainChannels = new List<TimeDomainChannel>(4);

            //configure the 4 sense channels
            for (int iCount = 0; iCount < 4; iCount++)
            {
                TdMuxInputs anode, cathode;
                TdLpfStage1 stage1LPF;
                TdLpfStage2 stage2LPF;
                TdHpfs HPF;
                TdSampleRates rateOrDisabled = samplingRate;

                //first two channels are bore 1
                if (iCount <= 1)
                {
                    GetSenseParameters(bore1Anodes, bore1Cathodes, bore1Stage1LPFs, bore1Stage2LPFs, bore1HPFs,
                        iCount, out anode, out cathode, out stage1LPF, out stage2LPF, out HPF, ref rateOrDisabled);
                }
                else //last two channels are bore 2
                {
                    GetSenseParameters(bore2Anodes, bore2Cathodes, bore2Stage1LPFs, bore2Stage2LPFs, bore2HPFs,
                        iCount - 2, out anode, out cathode, out stage1LPF, out stage2LPF, out HPF, ref rateOrDisabled);
                }

                //make channel
                timeDomainChannels.Add(new TimeDomainChannel(
                rateOrDisabled,
                anode,
                cathode,
                TdEvokedResponseEnable.Standard,
                stage1LPF,
                stage2LPF,
                HPF));

            }
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Helper function for ConfigureTimeDomain() for getting the sense channel
        ///             parameters from a list of parameters.
        ///             </summary>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="anodes">           The anodes. </param>
        /// <param name="cathodes">         The cathodes. </param>
        /// <param name="Stage1LPFs">       The stage 1 pointer to a file system. </param>
        /// <param name="Stage2LPFs">       The stage 2 pointer to a file system. </param>
        /// <param name="HPFs">             The hp file system. </param>
        /// <param name="channelNum">       The channel number. </param>
        /// <param name="theAnode">         [out] the anode. </param>
        /// <param name="theCathode">       [out] the cathode. </param>
        /// <param name="theLPF1">          [out] The first the lpf. </param>
        /// <param name="theLPF2">          [out] The second the lpf. </param>
        /// <param name="theHPF">           [out] the hpf. </param>
        /// <param name="theSamplingRate">  [in,out] the sampling rate. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void GetSenseParameters(List<int> anodes, List<int> cathodes, List<int> Stage1LPFs, List<int> Stage2LPFs,
            List<double> HPFs, int channelNum, out TdMuxInputs theAnode, out TdMuxInputs theCathode, out TdLpfStage1 theLPF1,
            out TdLpfStage2 theLPF2, out TdHpfs theHPF, ref TdSampleRates theSamplingRate)
        {

            //Check if the channel is being used or not
            if (channelNum >= anodes.Count)
            {
                //change the sampling rate to disabled, set the other parameters to whatever
                theAnode = TdMuxInputs.Floating;
                theCathode = TdMuxInputs.Floating;
                theSamplingRate = TdSampleRates.Disabled;
                theLPF1 = TdLpfStage1.Lpf50Hz;
                theLPF2 = TdLpfStage2.Lpf100Hz;
                theHPF = TdHpfs.Hpf0_85Hz;
            }
            else
            {
                //get anode and cathode
                try
                {
                    //value of 16 or 8 means use floating (case)
                    theAnode = (anodes[channelNum] == 16) || (anodes[channelNum] == 8) ?
                        TdMuxInputs.Floating : (TdMuxInputs)Enum.Parse(typeof(TdMuxInputs), "Mux" + anodes[channelNum]);
                }
                catch
                {
                    throw new Exception(String.Format("Chosen Anode channel: {0}, isn't a valid selection, check JSON file specifications!",
                        anodes[channelNum]));
                }

                try
                {
                    //value of 16 means use floating (case)
                    theCathode = (cathodes[channelNum] == 16) || (cathodes[channelNum] == 8) ?
                        TdMuxInputs.Floating : (TdMuxInputs)Enum.Parse(typeof(TdMuxInputs), "Mux" + cathodes[channelNum]);
                }
                catch
                {
                    throw new Exception(String.Format("Chosen Cathode channel: {0}, isn't a valid selection, check JSON file specifications!",
                        cathodes[channelNum]));
                }

                try
                {
                    theLPF1 = (TdLpfStage1)Enum.Parse(typeof(TdLpfStage1), "Lpf" + Stage1LPFs[channelNum] + "Hz");
                }
                catch
                {
                    throw new Exception(String.Format("Chosen Stage 1 lowpass: {0}, isn't a valid selection, check JSON file specifications!",
                        Stage1LPFs[channelNum]));
                }

                try
                {
                    theLPF2 = (TdLpfStage2)Enum.Parse(typeof(TdLpfStage2), "Lpf" + Stage2LPFs[channelNum] + "Hz");
                }
                catch
                {
                    throw new Exception(String.Format("Chosen Stage 2 lowpass: {0}, isn't a valid selection, check JSON file specifications!",
                        Stage2LPFs[channelNum]));
                }

                try
                {
                    //since high pass cutoff can be a decimal number, have to convert it to Medtronic's enums names (e.g. 1.2 -> Hpf1_2Hz)
                    double HPvalue = HPFs[channelNum];
                    string enumName = "Hpf" + Math.Floor(HPFs[channelNum]) + "_";

                    //keep adding decimal digits until we got all of them
                    //**(note that if in the future Medtroic decides to add redundent 0's to the name (e.g. 1 -> Hpf1_0Hz), this will need to be changed**
                    int decimalPos = 10;
                    while ((Math.Floor(HPvalue * decimalPos/10) - HPvalue * decimalPos/10) != 0)
                    {
                        //get digit, add to string
                        enumName += Math.Floor((HPvalue * decimalPos - Math.Floor(HPvalue * decimalPos / 10) * 10));
                        
                        //go to next decimal place
                        decimalPos *= 10;
                    }


                    theHPF = (TdHpfs)Enum.Parse(typeof(TdHpfs), enumName+"Hz");
                }
                catch
                {
                    throw new Exception(String.Format("Chosen highpass: {0}, isn't a valid selection, check JSON file specifications!",
                        HPFs[channelNum]));
                }
            }

        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Function to make FFT channel configuration from the JSON parameters. </summary>
        ///
        /// <param name="parameters">   . </param>
        /// <param name="FFTConfig">    [out] The FFT configuration. </param>
        /// <param name="FFTChannel">   [out] The FFT channel. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void ConfigureFFT(INSParameters parameters, out FftConfiguration FFTConfig, out SenseTimeDomainChannel FFTChannel)
        {
            FFTConfig = new FftConfiguration();
            FFTChannel = new SenseTimeDomainChannel();

            //get parameters
            FftParameters fftParams;

            GetFFTParameters(parameters, out fftParams);

            //set configuration
            FFTConfig.Size = fftParams.size;
            FFTConfig.Interval = (ushort)fftParams.interval;
            FFTConfig.WindowEnabled = fftParams.windowEnabled;
            FFTConfig.WindowLoad = fftParams.windowLoad;
            FFTConfig.StreamSizeBins = (ushort)fftParams.binSize;
            FFTConfig.StreamOffsetBins = (ushort)fftParams.binOffset;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Helper function for ConfigureFFT() to get the FFT parameters with error checking.
        ///             </summary>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="parameters">   . </param>
        /// <param name="fftParams">    [out] Options for controlling the FFT. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void GetFFTParameters(INSParameters parameters, out FftParameters fftParams)
        {
            //get value from parameters

            //first FFT Size
            int paramSize = parameters.GetParam("Sense.FFT.FFTSize", typeof(int));
            //parse into the Medtronic Enum definition
            try
            {
                fftParams.size = (FftSizes)Enum.Parse(typeof(FftSizes), "Size" + paramSize.ToString("0000"));
            }
            catch
            {
                throw new Exception(String.Format("Chosen FFT size: {0}, isn't a valid selection, check JSON file specifications!",
                    paramSize));
            }

            //next get the time domain channel that you want to stream the FFT from
            int paramChannel = parameters.GetParam("Sense.FFT.Channel", typeof(int));
            try
            {
                fftParams.channel = (SenseTimeDomainChannel)Enum.Parse(typeof(SenseTimeDomainChannel), "Ch" + paramChannel);
            }
            catch
            {
                throw new Exception(String.Format("Chosen FFT channel: {0}, isn't a valid selection, check JSON file specifications!",
                    paramChannel));
            }

            //get FFT interval
            fftParams.interval = parameters.GetParam("Sense.FFT.FFTInterval", typeof(int));

            //get whether to window FFT
            fftParams.windowEnabled = parameters.GetParam("Sense.FFT.WindowEnabled", typeof(bool));

            //get window load
            int paramWindowLoad = parameters.GetParam("Sense.FFT.WindowLoad", typeof(int));
            try
            {
                fftParams.windowLoad = (FftWindowAutoLoads)Enum.Parse(typeof(FftWindowAutoLoads), "Hann" + paramWindowLoad);
            }
            catch
            {
                throw new Exception(String.Format("Chosen FFT window load: {0}, isn't a valid selection, check JSON file specifications!",
                    paramWindowLoad));
            }

            //get how many bins to stream
            fftParams.binSize = parameters.GetParam("Sense.FFT.StreamSizeBins", typeof(int));

            //get which bin index to start streaming from
            fftParams.binOffset = parameters.GetParam("Sense.FFT.StreamOffsetBins", typeof(int));

        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Function to make Power band configurations from the JSON parameters. </summary>
        ///
        /// <param name="parameters">       . </param>
        /// <param name="indexInJSON">      The index in JSON. </param>
        /// <param name="powerChannels">    [out] The power channels. </param>
        /// <param name="theBandEnables">   [out] the band enables. </param>
        /// <param name="powerEnabled">     [out] True to enable, false to disable the power. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void ConfigurePower(INSParameters parameters, List<int?> indexInJSON, out List<PowerChannel> powerChannels,
            out BandEnables theBandEnables, out bool powerEnabled)
        {
            // get list of parameters for the channels
            var paramBand1Enabled = parameters.GetParam("Sense.BandPower.FirstBandEnabled", typeof(bool));
            var paramBand2Enabled = parameters.GetParam("Sense.BandPower.SecondBandEnabled", typeof(bool));
            var paramBand1Lower = parameters.GetParam("Sense.BandPower.FirstBandLower", typeof(int));
            var paramBand1Upper = parameters.GetParam("Sense.BandPower.FirstBandUpper", typeof(int));
            var paramBand2Lower = parameters.GetParam("Sense.BandPower.SecondBandLower", typeof(int));
            var paramBand2Upper = parameters.GetParam("Sense.BandPower.SecondBandUpper", typeof(int));

            //go through all four time domain channels and define the power channels
            powerChannels = new List<PowerChannel>(4);
            theBandEnables = new BandEnables();
            bool firstBand = true; //need this to know whether to set theBandEnables or to add to it

            for (int iChan = 0; iChan < 4; iChan++)
            {
                //first see if this channel was even defined in the JSON
                if (indexInJSON[iChan] == null)
                {
                    powerChannels.Add(new PowerChannel(0, 0, 0, 0));
                    continue;
                }

                //next see if we wanted to enable either band for this channel, and if so, add it to theBandEnables
                List<bool> powerBandsEnabled = new List<bool>();
                powerBandsEnabled.Add(paramBand1Enabled[indexInJSON[iChan].Value]);
                powerBandsEnabled.Add(paramBand2Enabled[indexInJSON[iChan].Value]);

                //go through both bands and add the enable enums
                for (int iBand = 0; iBand < 2; iBand++)
                {
                    if (powerBandsEnabled[iBand])
                    {
                        if (firstBand) //just set it if it hasn't been defined yet
                        {
                            theBandEnables = (BandEnables)Enum.Parse(typeof(BandEnables), "Ch" + iChan + "Band" + iBand + "Enabled");
                            firstBand = false;
                        }
                        else //if other than first chan, add to it
                        {
                            theBandEnables = theBandEnables | (BandEnables)Enum.Parse(typeof(BandEnables), "Ch" + iChan + "Band" + iBand + "Enabled");
                        }
                    }
                }

                //finally add the power band limits
                powerChannels.Add(new PowerChannel((ushort)paramBand1Lower[indexInJSON[iChan].Value], (ushort)paramBand1Upper[indexInJSON[iChan].Value],
                    (ushort)paramBand2Lower[indexInJSON[iChan].Value], (ushort)paramBand2Upper[indexInJSON[iChan].Value]));
            }

            if (firstBand) //that means no bands were used, so power band is disabled
            {
                powerEnabled = false;
            }
            else
            {
                powerEnabled = true;
            }

        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Function to get the value from the JSON parameters where the parameter field may
        ///             or may not be an array of values. Takes in an index to get the value if it is an
        ///             array.
        ///             </summary>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="parameters">   . </param>
        /// <param name="pathToParam">  . </param>
        /// <param name="paramType">    . </param>
        /// <param name="index">        . </param>
        ///
        /// <returns>   Returns the requested value from the JSON parameters. </returns>
        ///-------------------------------------------------------------------------------------------------
        public static dynamic GetParamValueFromPossibleArray(INSParameters parameters, string pathToParam, Type paramType, int? index)
        {
            if (parameters.ParamIsArray(pathToParam))
            {
                if (index == null)
                {
                    throw new Exception(String.Format("{0} is an array, but no index is given!", pathToParam));
                }
                return parameters.GetParam(pathToParam, paramType, index);
            }
            else
            {
                return parameters.GetParam(pathToParam, paramType);
            }
        }

        

        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Helper function to convert frequency values in Hz to the Medtronic RatePeriod (in
        ///             10us). Some precision might be lost due to rounding (will show a warning).
        ///             </summary>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="freq">             The frequency. </param>
        /// <param name="ratePeriod">       [out] The rate period. </param>
        /// <param name="displayLocation">  (Optional) The display location. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void ConvertHzToRatePeriod(double freq, out ushort ratePeriod, string displayLocation = "")
        {
            //get rate period (in 10us) from  frequency (in Hz), 
            //convert some precision might be lost due to rounding
            long longRatePeriod = (long)Math.Round(100000 / freq);

            //warn user about precision lose:
            double offsetPeriod = 100000 / freq - (double)longRatePeriod;
            if (offsetPeriod > 0.00001)
            {
                double offsetFreq = 100000 / offsetPeriod;
                Console.WriteLine(String.Format("Warning: Converting frequency {0}Hz to 10us rate period is off by {1:0.00000}Hz due to rounding! ", freq, offsetFreq) + displayLocation);

            }

            //check for overflow
            if (longRatePeriod > 65535)
            {
                throw new Exception(String.Format("Freqeuncy value {1}Hz when converting to 10us rate period is out of range (ushort)!", freq) + displayLocation);
            }

            //convert and return
            ratePeriod = (ushort)longRatePeriod;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Helper function to convert amplitude values in milliAmps as doubles to the
        ///             Medtronic Amplitude (ints at 0.1mA). Some precision might be lost if the inputted
        ///             values are too high precision (will show a warning).
        ///             </summary>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="inAmplitude">      The in amplitude. </param>
        /// <param name="outAmplitude">     [out] The out amplitude. </param>
        /// <param name="displayLocation">  (Optional) The display location. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void ConvertAmplitude(double inAmplitude, out int outAmplitude, string displayLocation = "")
        {
            //convert to 0.1 mAs
            double ampIn0_1mAs = Math.Round(inAmplitude * 10);

            //warn user about precision lose:
            double offsetAmp = inAmplitude * 10 - ampIn0_1mAs;
            if (offsetAmp > 0.00001)
            {
                Console.WriteLine(String.Format("Warning: Converting current amplitude {0}mA to 0.1mA ints is off by {1:0.00000}mA, input precision is too high! ", inAmplitude, offsetAmp/10) + displayLocation);

            }

            //check for overflow
            if (ampIn0_1mAs > 127 || ampIn0_1mAs < -128)
            {
                throw new Exception(String.Format("Amplitude value {1}mA out of range (byte)!", inAmplitude) + displayLocation);
            }

            //convert and return
            outAmplitude = (int)ampIn0_1mAs;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Helper function to convert values in microseconds to 10's of microseconds (pulse
        ///             widths and rate periods). Some precision might be lost if the inputted values are
        ///             too high precision (will show a warning).
        ///             </summary>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="inValue">          The in value. </param>
        /// <param name="outValue">         [out] The out value. </param>
        /// <param name="valueType">        Type of the value. </param>
        /// <param name="displayLocation">  (Optional) The display location. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void ConvertPrecisionOf10(int inValue, out int outValue, string valueType, string displayLocation = "")
        {
            //convert to 10's of us
            double valueDouble = Math.Round((double)inValue / 10);

            //warn user about precision lose:
            double offsetValue= (double)inValue / 10 - valueDouble;
            if (offsetValue > 0.00001)
            {
                Console.WriteLine(String.Format("Warning: Converting {0} {1}us to 10's of us is off by {2:0.00000}us, input precision is too high! ", valueType, inValue, offsetValue * 10) + displayLocation);

            }

            //check for overflow
            double maxValue = 127;
            if (valueType == "Rate Period" || valueType == "RatePeriod")
            {
                maxValue = 65535;
            }
            if (valueDouble > maxValue)
            {
                throw new Exception(String.Format("{0} value {1}us out of range (byte)!", valueType, inValue) + displayLocation);
            }

            //convert and return
            outValue = (int)valueDouble;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Check current stim parameters. </summary>
        ///
        /// <param name="summit">                   The summit. </param>
        /// <param name="groupNum">                 The group number. </param>
        /// <param name="iProgram">                 Zero-based index of the program. </param>
        /// <param name="ampMilliamps">             [out] The amp milliamps. </param>
        /// <param name="pulseWidthMicroseconds">   [out] The pulse width microseconds. </param>
        /// <param name="freqHz">                   [out] The frequency Hz. </param>
        ///
        /// <returns>   True if it succeeds, false if it fails. </returns>
        ///-------------------------------------------------------------------------------------------------
        public static bool CheckCurrentStimParameters(SummitSystem summit, GroupNumber groupNum, int iProgram, 
            out double? ampMilliamps, out int? pulseWidthMicroseconds, out double? freqHz)
        {
            TherapyGroup groupInfo;
            summit.ReadStimGroup(groupNum, out groupInfo);

            if (groupInfo == null)
            {
                ampMilliamps = null;
                pulseWidthMicroseconds = null;
                freqHz = null;
                Console.WriteLine("Error in getting stim parameters! Recommend restarting program.");
                return false;
            }

            ampMilliamps = groupInfo.Programs[iProgram].AmplitudeInMilliamps;
            pulseWidthMicroseconds = groupInfo.Programs[iProgram].PulseWidthInMicroseconds;
            freqHz = groupInfo.RateInHz;

            return true;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Function which returns the number of dropped packets based on packet numbers of
        ///             current and past packet. Checks for looping and packet jumbling.
        ///             </summary>
        ///
        /// <param name="newPacketNum">     The packet packet number of the newly received packet. </param>
        /// <param name="prevPacketNum">    The packet number of the previous packet. </param>
        /// <param name="nDroppedPackets">  [out] Outputs the number of dropped packets. </param>
        ///
        /// <returns>   True if it succeeds, false if it fails. </returns>
        ///-------------------------------------------------------------------------------------------------
        public static bool CheckDroppedPackets(int newPacketNum, int prevPacketNum, out int nDroppedPackets)
        {
            nDroppedPackets = 0;
            int packetNumDiff = newPacketNum - prevPacketNum;
            
            if (packetNumDiff < 0)
            {
                //the difference can be negative for two reasons: looping from 255 to 0, or a "future" packet was accidentally sent first

                //Check if a future packet was sent first, using a tolerance window of 10 for now, might need to be increased
                if (packetNumDiff > -10) 
                {
                    //probably what happend was the packet numbers got jumbled
                    // e.g. 0 1 2 5 3 4 6 7 8
                    //            ^----^ packet mixed up
                    Console.WriteLine(String.Format(
                        "Packet number is less than the previous packet number, but within 10 of the previous packet number! This packet num: {0}, prev: {1}",
                        newPacketNum, prevPacketNum));
                    
                    //if this is the case, I will just ignore "future" packets for now (just don't do any thing with the packet)
                    return false;
                }

                //otherwise it was a loop
                nDroppedPackets = packetNumDiff + 256 - 1;
            }
            //Check if a future packet was sent first, and the future packet was post 255->0 loop (using a tolerance window of 10 for now, might need to be increased)
            else if (packetNumDiff > 245) 
            {
                //probably what happend was the packet numbers got jumbled and a packet number from after it loops came before the numbers before it looped
                // e.g. 253 254 2 255 0 1 3 4 5
                //              ^--------^ packet mixed up
                Console.WriteLine("Packet number is more than 245 more than the previous packet number!");
                
                //ignore the packet
                return false;

            }
            else if ((newPacketNum - prevPacketNum) == 0)
            {
                //This should never happen!!
                Console.WriteLine("Warning: Same TD packet number received on two different packets!");
                return false;
            }
            else
            {
                //normal case (no looping, or future packets getting jumbled)
                nDroppedPackets = packetNumDiff - 1;
            }

            return true;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Interpolate dropped samples. </summary>
        ///
        /// <param name="TDBuffer">         Buffer for td data. </param>
        /// <param name="dataSavingBuffer"> Buffer for data saving data. </param>
        /// <param name="TdSenseEvent">     The td sense event. </param>
        /// <param name="interpParams">     Options for controlling the interp. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void InterpolateDroppedSamples(INSBuffer TDBuffer, INSBuffer dataSavingBuffer, SensingEventTD TdSenseEvent, InterpolationParameters interpParams)
        {
            //lock the data buffers until we add the interpolated data
            TDBuffer.lockWriter();
            dataSavingBuffer.lockWriter();

                Console.WriteLine(String.Format("Interpolating dropped sample"));

            //estimate how many samples were dropped
            double elapsedTime; //first get the elapsed time
            if (interpParams.timestampDiff <= 0)
            {
                //timestamp loop case
                elapsedTime = (double)(interpParams.timestampDiff + 65536) / interpParams.secondsToTimeStamp; //time in seconds

                if (interpParams.timestampDiff > -60000)
                {
                    Console.WriteLine("More than 5 seconds have elapsed! Might be fishy");
                }
            }
            else
            {
                elapsedTime = (double)interpParams.timestampDiff / interpParams.secondsToTimeStamp; //time in seconds
            }

            int elapsedSamples = 0;

            //number of samples that we should have is the elapsed time since last packet multiplied by the sampling rate
            switch (interpParams.samplingRate)
            {
                case TdSampleRates.Sample0250Hz:
                    elapsedSamples = (int)Math.Round(elapsedTime * 250);
                    break;

                case TdSampleRates.Sample0500Hz:
                    elapsedSamples = (int)Math.Round(elapsedTime * 500);
                    break;

                case TdSampleRates.Sample1000Hz:
                    elapsedSamples = (int)Math.Round(elapsedTime * 1000);
                    break;
            }

            int nMissingSamples = elapsedSamples - interpParams.prevPacketNSamples;
            if (nMissingSamples < interpParams.nDroppedPackets)
            {
                //this should never happen
                nMissingSamples = interpParams.nDroppedPackets;
                Console.WriteLine(String.Format("Something wrong with estimating dropped packet samples!"));
            }

            //fill in missing samples with linearly interpolated values
            double[] interpSlope = new double[interpParams.nChans];
            double[] interpIntercept = new double[interpParams.nChans];

            {
                int iChan = 0;
                foreach (SenseTimeDomainChannel chan in TdSenseEvent.ChannelSamples.Keys) //(SenseTimeDomainChannel chan in Enum.GetValues(typeof(SenseTimeDomainChannel)))
                {
                    interpSlope[iChan] = (TdSenseEvent.ChannelSamples[chan][0] - interpParams.prevValues[iChan]) / (nMissingSamples + 1);
                    interpIntercept[iChan] = interpParams.prevValues[iChan];
                    iChan++;
                }
            }

            //Now add interpolated values to buffer. Put in as many packets as were dropped, with 1 sample in each packet until
            //the last packet which will contain the rest of the samples (we don't know how the dropped samples are divided amongst
            //the dropped packets, but this way at least each dropped packet is represented in the buffer).

            //estimate the beginning timestamp of the interpolated values
            double droppedTimestamp = (double)interpParams.prevPacketTime + interpParams.prevPacketNSamples * (elapsedTime * interpParams.secondsToTimeStamp) / elapsedSamples;

            for (int iPacket = 0; iPacket < interpParams.nDroppedPackets; iPacket++)
            {

                if (iPacket != interpParams.nDroppedPackets - 1) //for all dropped packets except the last one, just add one sample
                {
                    double[,] interpChanData = new double[interpParams.nChans, 1];

                    for (int iChan = 0; iChan < interpParams.nChans; iChan++)
                    {
                        interpChanData[iChan, 0] = interpSlope[iChan] * (iPacket + 1) + interpIntercept[iChan];
                    }

                    TDBuffer.addData(interpChanData, iPacket + interpParams.prevPacketNum + 1, 
                        droppedTimestamp + iPacket * (elapsedTime * interpParams.secondsToTimeStamp) / elapsedSamples, 1, true);
                    dataSavingBuffer.addData(interpChanData, iPacket + interpParams.prevPacketNum + 1,
                        droppedTimestamp + iPacket * (elapsedTime * interpParams.secondsToTimeStamp) / elapsedSamples, 1, true);
                }

                else //fill the last dropped packet with the rest of the interpolated samples
                {
                    double[,] interpChanData = new double[interpParams.nChans, nMissingSamples - interpParams.nDroppedPackets + 1];

                    for (int iSample = interpParams.nDroppedPackets - 1; iSample < nMissingSamples; iSample++)
                    {

                        for (int iChan = 0; iChan < interpParams.nChans; iChan++)
                        {
                            interpChanData[iChan, iSample - interpParams.nDroppedPackets + 1] = interpSlope[iChan] * (iSample + 1) + interpIntercept[iChan];
                        }

                    }

                    TDBuffer.addData(interpChanData, iPacket + interpParams.prevPacketNum + 1,
                        droppedTimestamp + iPacket * (elapsedTime * interpParams.secondsToTimeStamp) / elapsedSamples, 1, true);
                    dataSavingBuffer.addData(interpChanData, iPacket + interpParams.prevPacketNum + 1,
                        droppedTimestamp + iPacket * (elapsedTime * interpParams.secondsToTimeStamp) / elapsedSamples, 1, true);
                }

            }

            //unlock the data buffers
            TDBuffer.unlockWriter();
            dataSavingBuffer.unlockWriter();
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Get device information such as battery, sense/stim status, sense/stim config, 
        ///             ect. </summary>
        ///
        /// <param name="theSummit">        Buffer for td data. </param>
        /// <param name="payload">          Buffer for data saving data. </param>
        /// <param name="TdSenseEvent">     The td sense event. </param>
        /// <param name="interpParams">     Options for controlling the interp. </param>
        /// 
        /// <returns>   True if it succeeds, false if it fails. </returns>
        ///-------------------------------------------------------------------------------------------------
        public static bool ConvertEnumsToValues(string enumName, string enumType, out double value)
        {
            int nChars = enumName.Length;
            value = 0;

            switch (enumType)
            {
                case "TdMuxInputs":
                    //it's a time domain electrode enum
                    if (!Enum.IsDefined(typeof(TdMuxInputs), enumName))
                    {
                        return false;
                    }

                    if (enumName == "Floating")
                    {
                        value = 16;
                        return true;
                    }
                    else
                    {
                        if (!double.TryParse(enumName.Substring(3), out value))
                        {
                            return false;
                        }

                        return true;
                    }
                    break;


                case "TdLpfStage1":
                case "TdLpfStage2":
                    //it's a stage 1 LPF value, remove the "lpf" and the "hz" parts
                    if (!Enum.IsDefined(typeof(TdLpfStage1), enumName) && !Enum.IsDefined(typeof(TdLpfStage2), enumName))
                    {
                        return false;
                    }

                    if (!double.TryParse(enumName.Substring(3, nChars - 5), out value))
                    {
                        return false;
                    }
                    return true;
                    break;


                case "TdHpfs":
                    //it's a stage HPF value, remove the "hpf" and the "hz" parts, and replace "_" with "."
                    if (!Enum.IsDefined(typeof(TdHpfs), enumName))
                    {
                        return false;
                    }

                    if (!double.TryParse(enumName.Substring(3, nChars - 5).Replace('_', '.'), out value))
                    {
                        return false;
                    }
                    //for some reason it parses 0.85 as some number really close to 0.85 but not exactly
                    //value = Convert.ToSingle(Math.Round(value, 3));
                    return true;
                    break;


                case "TdSampleRates":
                    //it's a sampling rate, remove the "Sample" and the "hz" parts
                    if (!Enum.IsDefined(typeof(TdSampleRates), enumName))
                    {
                        return false;
                    }

                    if (enumName == "Disabled")
                    {
                        value = 0;
                        return true;
                    }
                    if (!double.TryParse(enumName.Substring(6, nChars - 8), out value))
                    {
                        return false;
                    }
                    return true;
                    break;


                case "FftSizes":
                    //it's a FFT window size, remove the "Size" part
                    if (!Enum.IsDefined(typeof(FftSizes), enumName))
                    {
                        return false;
                    }

                    if (!double.TryParse(enumName.Substring(4, nChars - 4), out value))
                    {
                        return false;
                    }
                    return true;
                    break;


                case "GroupNumber":
                    //enum for stim group number, just remove the "Group" part
                    if (!Enum.IsDefined(typeof(GroupNumber), enumName))
                    {
                        return false;
                    }

                    if (!double.TryParse(enumName.Substring(5), out value))
                    {
                        return false;
                    }
                    return true;
                    break;


                case "InterrogateTherapyStatusTypes":
                    //enum for stim group number, just remove the "Group" part
                    if (!Enum.IsDefined(typeof(InterrogateTherapyStatusTypes), enumName))
                    {
                        return false;
                    }

                    if (enumName == "TherapyActive" || enumName == "TransitionToActive")
                    {
                        value = 1;
                        return true;
                    }
                    else
                    {
                        value = 0;
                        return true;
                    }
                    break;



                case "ProgramEnables":
                    //enum for whether a stim program is enabled or not
                    if (!Enum.IsDefined(typeof(ProgramEnables), enumName))
                    {
                        return false;
                    }

                    if (enumName.Contains("Enabled"))
                    {
                        value = 1;
                        return true;
                    } 
                    else if (enumName.Contains("Disabled"))
                    {
                        value = 0;
                        return true;
                    }
                    else
                    {
                        value = 0;
                        return true;
                    }
                    break;


                case "ActiveRechargeRatios":
                    //if it's active vs passive recharge
                    if (!Enum.IsDefined(typeof(ActiveRechargeRatios), enumName))
                    {
                        return false;
                    }

                    if (enumName == "PassiveOnly")
                    {
                        value = 0;
                        return true;
                    }
                    else
                    {
                        value = 1;
                        return true;
                    }
                    break;

            }

            return false;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Get device information such as battery, sense/stim status, sense/stim config, 
        ///             ect. </summary>
        ///
        /// <param name="theSummit">        The summit object to talk to the INS. </param>
        /// <param name="payload">          Output Payload structure, for sending to MyRcpS. </param>
        /// <param name="parseErrorCode">   Indicates whether there was an error in the parsing of the
        ///                                 data from the INS (rather than an error in talking to the
        ///                                 INS). 
        ///                                 0 - No error in parsing (though still could have INS error)
        ///                                 1 - Error in parsing Enums
        ///                                 2 - # of power bands != # of time domain channels 
        ///                                 3 - found two anodes for a stim program
        ///                                 4 - found two cathodes for a stim program
        ///                                 5 - couldn't find anode or cathode for a stim program </param>
        /// 
        /// <returns>   The summit error code (which could be no error). </returns>
        ///-------------------------------------------------------------------------------------------------
        public static APIReturnInfo QueryDeviceStatus(SummitSystem theSummit, out StreamingThread.MyRCSMsg.Payload payload, out int parseErrorCode)
        {
            payload = new StreamingThread.MyRCSMsg.Payload();
            parseErrorCode = 0;

            //run queries using the summit API functions
            APIReturnInfo commandInfo = new APIReturnInfo();

            //first get sense info
            SensingConfiguration sensingConfig = new SensingConfiguration();
            commandInfo = theSummit.ReadSensingSettings(out sensingConfig);
            if (commandInfo.RejectCode != 0)
            {
                return commandInfo;
            }

            //parse sense config values
            //time domain config
            string enumName;
            int nChannels = sensingConfig.TimeDomainChannels.Count();
            for (int iChan = 0; iChan <= nChannels - 1; iChan++)
            {

                //first, see which contacts are used for each channel
                int boreOffset = 0;
                if (iChan > 1)
                {
                    boreOffset = 8;
                }

                double anodeChan, cathodeChan;

                enumName = sensingConfig.TimeDomainChannels[iChan].PlusInput.ToString();
                if (!ConvertEnumsToValues(enumName, "TdMuxInputs", out anodeChan))
                {
                    parseErrorCode = 1;
                    return commandInfo;
                }
                if (anodeChan != 16)
                {
                    anodeChan = anodeChan + boreOffset;
                }

                enumName = sensingConfig.TimeDomainChannels[iChan].MinusInput.ToString();
                if(!ConvertEnumsToValues(enumName, "TdMuxInputs", out cathodeChan))
                {
                    parseErrorCode = 1;
                    return commandInfo;
                }
                if (cathodeChan != 16)
                {
                    cathodeChan = cathodeChan + boreOffset;
                }

                payload.sense_config.anodes.Add(Convert.ToUInt16(anodeChan));
                payload.sense_config.cathodes.Add(Convert.ToUInt16(cathodeChan));

                //next get the filter values for each channel
                double lpf1Value, lpf2Value, hpfValue;

                enumName = sensingConfig.TimeDomainChannels[iChan].Lpf1.ToString();
                if(!ConvertEnumsToValues(enumName, "TdLpfStage1", out lpf1Value))
                {
                    parseErrorCode = 1;
                    return commandInfo;
                }
                payload.sense_config.lowpass_filter1.Add(Convert.ToUInt16(lpf1Value));

                enumName = sensingConfig.TimeDomainChannels[iChan].Lpf2.ToString();
                if(!ConvertEnumsToValues(enumName, "TdLpfStage2", out lpf2Value))
                {
                    parseErrorCode = 1;
                    return commandInfo;
                }
                payload.sense_config.lowpass_filter2.Add(Convert.ToUInt16(lpf2Value));

                enumName = sensingConfig.TimeDomainChannels[iChan].Hpf.ToString();
                if(!ConvertEnumsToValues(enumName, "TdHpfs", out hpfValue))
                {
                    parseErrorCode = 1;
                    return commandInfo;
                }
                payload.sense_config.highpass_filter.Add(hpfValue);
                
                //then the sampling rates
                double samplingRate;
                enumName = sensingConfig.TimeDomainChannels[iChan].SampleRate.ToString();
                if(!ConvertEnumsToValues(enumName, "TdSampleRates", out samplingRate))
                {
                    parseErrorCode = 1;
                    return commandInfo;
                }
                payload.sense_config.sampling_rates.Add(Convert.ToUInt16(samplingRate));
            }

            //fft config
            double fftSize, fftWindowLoad, fftStreamSize, fftStreamOffset;

            enumName = sensingConfig.FftConfig.Size.ToString();
            if (!ConvertEnumsToValues(enumName, "FftSizes", out fftSize))
            {
                parseErrorCode = 1;
                return commandInfo;
            }
            payload.sense_config.FFT_size = Convert.ToUInt16(fftSize);

            payload.sense_config.FFT_interval = sensingConfig.FftConfig.Interval;
            payload.sense_config.FFT_windowing_on = sensingConfig.FftConfig.WindowEnabled;
            payload.sense_config.FFT_window_load = sensingConfig.FftConfig.WindowLoad.ToString();
            payload.sense_config.FFT_stream_size = sensingConfig.FftConfig.StreamSizeBins;
            payload.sense_config.FFT_stream_offset = sensingConfig.FftConfig.StreamOffsetBins;
            
            //now get the power bands
            if (sensingConfig.PowerChannels.Count() != nChannels)
            {
                //the number of power band channels must equal the number of time domain channels
                parseErrorCode = 2;
                return commandInfo;
            }
            
            for (int iChan = 0; iChan <= nChannels - 1; iChan++)
            {
                payload.sense_config.powerband1_lower_cutoff.Add(sensingConfig.PowerChannels[iChan].Band0Start);
                payload.sense_config.powerband1_upper_cutoff.Add(sensingConfig.PowerChannels[iChan].Band0Stop);
                payload.sense_config.powerband2_lower_cutoff.Add(sensingConfig.PowerChannels[iChan].Band1Start);
                payload.sense_config.powerband2_upper_cutoff.Add(sensingConfig.PowerChannels[iChan].Band1Stop);

                if (!Enum.TryParse<BandEnables>("Ch" + iChan.ToString() + "Band0Enabled", out BandEnables enabledEnum1) ||
                    !Enum.TryParse<BandEnables>("Ch" + iChan.ToString() + "Band1Enabled", out BandEnables enabledEnum2))
                {
                    //for some reason couldn't get the enum name, double check the enum values names
                    parseErrorCode = 1;
                    return commandInfo;
                }

                payload.sense_config.powerband1_enabled.Add(sensingConfig.BandEnable.HasFlag(enabledEnum1));
                payload.sense_config.powerband2_enabled.Add(sensingConfig.BandEnable.HasFlag(enabledEnum2));
            }

            SensingState sensingState = new SensingState();
            commandInfo = theSummit.ReadSensingState(out sensingState);
            commandInfo = theSummit.ReadSensingStreamState(out StreamState streamState);
            if (commandInfo.RejectCode != 0)
            {
                return commandInfo;
            }

            payload.sense_config.time_domain_on = streamState.TimeDomainStreamEnabled;
            payload.sense_config.FFT_on = streamState.FftStreamEnabled;
            payload.sense_config.accel_on = streamState.AccelStreamEnabled;
            payload.sense_config.powerbands_on = streamState.PowerDomainStreamEnabled;

            payload.sense_on = streamState.TimeDomainStreamEnabled || streamState.FftStreamEnabled || streamState.PowerDomainStreamEnabled;

            //next, get stimulation info
            GeneralInterrogateData insGeneralInfo;
            commandInfo = theSummit.ReadGeneralInfo(out insGeneralInfo);
            if (commandInfo.RejectCode != 0)
            {
                return commandInfo;
            }

            //the current active group
            enumName = insGeneralInfo.TherapyStatusData.ActiveGroup.ToString();
            double currentGroup;
            if (!ConvertEnumsToValues(enumName, "GroupNumber", out currentGroup))
            {
                parseErrorCode = 1;
                return commandInfo;
            }
            payload.stim_config.current_group = Convert.ToUInt16(currentGroup);

            //whether stim is currently on or not
            enumName = insGeneralInfo.TherapyStatusData.TherapyStatus.ToString();
            double stimOn;
            if (!ConvertEnumsToValues(enumName, "InterrogateTherapyStatusTypes", out stimOn))
            {
                parseErrorCode = 1;
                return commandInfo;
            }
            payload.stim_on = (stimOn == 1);

            //go through each therapy group
            TherapyGroup groupSettings = new TherapyGroup();
            AmplitudeLimits ampLimits = new AmplitudeLimits();
            
            foreach (GroupNumber iGroup in Enum.GetValues(typeof(GroupNumber)))
            {
                double thisGroupNum;
                if (!ConvertEnumsToValues(iGroup.ToString(), "GroupNumber", out thisGroupNum))
                {
                    parseErrorCode = 1;
                    return commandInfo;
                }
                int iGroupInd = Convert.ToInt16(thisGroupNum);

                commandInfo = theSummit.ReadStimGroup(iGroup, out groupSettings);
                if (commandInfo.RejectCode != 0)
                {
                    return commandInfo;
                }

                commandInfo = theSummit.ReadStimAmplitudeLimits(iGroup, out ampLimits);
                if (commandInfo.RejectCode != 0)
                {
                    return commandInfo;
                }

                //first get the program-indepdent configurations (pretty straight forward)
                payload.stim_config.pulsewidth_lower_limit.Add(Convert.ToUInt16(groupSettings.PulseWidthLowerLimitInMicroseconds));
                payload.stim_config.pulsewidth_upper_limit.Add(Convert.ToUInt16(groupSettings.PulseWidthUpperLimitInMicroseconds));
                payload.stim_config.frequency_lower_limit.Add(Convert.ToDouble(groupSettings.RateLowerLimitInHz));
                payload.stim_config.frequency_upper_limit.Add(Convert.ToDouble(groupSettings.RateUpperLimitInHz));
                payload.stim_config.current_frequency.Add(Convert.ToDouble(groupSettings.RateInHz));

                //now add the program-specific info
                payload.stim_config.anodes.Add(new List<ushort>());
                payload.stim_config.cathodes.Add(new List<ushort>());
                payload.stim_config.current_pulsewidth.Add(new List<ushort>());
                payload.stim_config.amplitude_lower_limit.Add(new List<double>());
                payload.stim_config.amplitude_upper_limit.Add(new List<double>());
                payload.stim_config.current_amplitude.Add(new List<double>());
                payload.stim_config.active_recharge.Add(new List<bool>());

                UInt16 nPrograms = 0;
                for (int iProg = 0; iProg < groupSettings.Programs.Count(); iProg++)
                {
                    //first determine if the program is defined (for some reason, it seems like there's always 4 programs
                    //even when less than 4 are defined. It'll just put the defined ones first and set the rest as disabled).
                    //So I'm assuming disabled is equivalent to undefined.
                    enumName = groupSettings.Programs[iProg].IsEnabled.ToString();
                    double enabled;
                    if (!ConvertEnumsToValues(enumName, "ProgramEnables", out enabled))
                    {
                        parseErrorCode = 1;
                        return commandInfo;
                    }
                    if (enabled==0)
                    {
                        //program isn't a real program that's been defined
                        continue;
                    }
                    else
                    {
                        nPrograms++;
                    }

                    //first, find out which channels are the anodes and cathodes for this program
                    TherapyElectrodes electrodesInfo = groupSettings.Programs[iProg].Electrodes;
                    
                    int anode = -1, cathode = -1;
                    for (int iElec = 0; iElec < electrodesInfo.Count; iElec++)
                    {
                        if (!electrodesInfo[iElec].IsOff && electrodesInfo[iElec].ElectrodeType == ElectrodeTypes.Anode)
                        {
                            if (anode != -1)
                            {
                                //an anode was already found, so we have two anodes which shouldn't happen
                                parseErrorCode = 3;
                                return commandInfo;
                            }
                            anode = iElec;
                        }
                        if (!electrodesInfo[iElec].IsOff && electrodesInfo[iElec].ElectrodeType == ElectrodeTypes.Cathode)
                        {
                            if (cathode != -1)
                            {
                                //an cathode was already found, so we have two cathode which shouldn't happen
                                parseErrorCode = 4;
                                return commandInfo;
                            }
                            cathode = iElec;
                        }
                    }

                    //if either anode or cathode hasn't been found, then something is wrong, otherwise, just add to the list
                    if (anode == -1 || cathode == -1)
                    {
                        parseErrorCode = 5;
                        return commandInfo;
                    }
                    payload.stim_config.anodes[iGroupInd].Add(Convert.ToUInt16(anode));
                    payload.stim_config.cathodes[iGroupInd].Add(Convert.ToUInt16(cathode));

                    //add the rest of the information
                    payload.stim_config.current_pulsewidth[iGroupInd].Add(Convert.ToUInt16(groupSettings.Programs[iProg].PulseWidthInMicroseconds));
                    payload.stim_config.current_amplitude[iGroupInd].Add(groupSettings.Programs[iProg].AmplitudeInMilliamps);
                    
                    enumName = groupSettings.Programs[iProg].MiscSettings.ActiveRechargeRatio.ToString();
                    if (!ConvertEnumsToValues(enumName, "ActiveRechargeRatios", out enabled))
                    {
                        parseErrorCode = 1;
                        return commandInfo;
                    }
                    payload.stim_config.active_recharge[iGroupInd].Add(enabled==1);

                    //now add the amplitude limits
                    object lowLimit = typeof(AmplitudeLimits).GetProperty("Prog" + iProg.ToString() + "LowerInMilliamps").GetValue(ampLimits, null);
                    object upLimit = typeof(AmplitudeLimits).GetProperty("Prog" + iProg.ToString() + "UpperInMilliamps").GetValue(ampLimits, null);
                    payload.stim_config.amplitude_lower_limit[iGroupInd].Add(Convert.ToDouble(lowLimit));
                    payload.stim_config.amplitude_upper_limit[iGroupInd].Add(Convert.ToDouble(upLimit));
                }

                //send the total number of valid programs
                payload.stim_config.number_of_programs.Add(nPrograms);
            }

            //finally get battery level
            payload.battery_level = Convert.ToUInt16(insGeneralInfo.BatteryStatus);

            return commandInfo;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Run impedance test across all combos of electrodes and case </summary>
        ///
        /// <param name="theSummit">        The summit object to talk to the INS. </param>
        /// <param name="impedFile">        File to save impedance values to. </param>
        /// <param name="enableTimeSync">   When turning on or off sensing, to enable time sync </param>
        /// 
        /// <returns>   The summit error code (which could be no error). </returns>
        ///-------------------------------------------------------------------------------------------------
        public static APIReturnInfo fullImpedanceTest(SummitSystem theSummit, System.IO.StreamWriter impedFile, bool enableTimeSync)
        {

            SensingState theSensingState;
            theSummit.ReadSensingState(out theSensingState);
            // Sensing must be turned off before a lead integrtiy test can be performed
            if (theSensingState.State != SenseStates.None)
            {
                APIReturnInfo commandInfo = theSummit.WriteSensingDisableStreams(true, true, true, false, false, true, enableTimeSync, false);
                if (commandInfo.RejectCode != 0)
                {
                    // Failed to turn off sensing!
                    Console.WriteLine("Failed to turn off sensing for impedance test.");
                    return commandInfo;
                }

            }

            Console.WriteLine("Running impedance test...");

            //make headers and labels
            impedFile.Write("Impedance Testing Time: " + String.Format("{0:F}", DateTime.Now) + "\r\n");
            impedFile.WriteLine();
            string impedHeader = "";
            for (int iElec = 0; iElec < 16; iElec++)
            {
                impedHeader += ("\tChan " + iElec);
            }

            impedFile.WriteLine(impedHeader);
            Console.WriteLine(impedHeader);
            APIReturnInfo testReturnInfo = new APIReturnInfo();

            // Performing impedance reading across all electrodes
            for (int iElec = 1; iElec < 17; iElec++)
            {
                //write row label
                if (iElec == 16)
                {
                    Console.Write("Case");
                    impedFile.Write("Case");
                }
                else
                {
                    Console.Write(String.Format("Chan {0}", iElec));
                    impedFile.Write(String.Format("Chan {0}", iElec));
                }

                List<double> thisElecImpedances = new List<double>(); //list to save impedances to
                List<Tuple<byte, byte>> elecPairs = new List<Tuple<byte, byte>>(); //list of pairs to try for this electrode

                //just want all combinations, not permutations, also don't want to test an electrode with itself
                for (int iElecPair = 0; iElecPair < iElec; iElecPair++)
                {
                    elecPairs.Add(new Tuple<byte, byte>((byte)iElec, (byte)iElecPair));
                }

                //get the impedances using summit function
                LeadIntegrityTestResult impedanceReadings;
                testReturnInfo = theSummit.LeadIntegrityTest(elecPairs, out impedanceReadings);

                // Make sure returned structure isn't null
                if (testReturnInfo.RejectCode == 0 && impedanceReadings != null)
                {
                    //store values
                    thisElecImpedances = impedanceReadings.PairResults.Where(o => o.Info != 0).Select(o => o.Impedance).ToList();

                    //write to console and file
                    thisElecImpedances.ForEach(i => Console.Write("\t{0}", i));
                    thisElecImpedances.ForEach(i => impedFile.Write("\t{0}", i));
                }
                else
                {
                    //write error message
                    Console.Write("\tError reading impedance values");
                    impedFile.Write("\tError reading impedance values");
                    return testReturnInfo;
                }

                Console.WriteLine();
                impedFile.WriteLine();
            }

            //finish
            Console.WriteLine("Finished impedance test");
            impedFile.Close();

            //turn sensing back on if it was turned off
            if (theSensingState.State != SenseStates.None)
            {
                APIReturnInfo commandInfo = theSummit.WriteSensingState(SenseStates.LfpSense | SenseStates.Fft | SenseStates.Power, 0x00);
                commandInfo = theSummit.WriteSensingEnableStreams(true, true, true, false, false, true, enableTimeSync, false);
            }


            return testReturnInfo;

        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Run impedance test across electrodes that are currently used for sensing 
        ///             or stim. </summary>
        ///
        /// <param name="theSummit">        The summit object to talk to the INS. </param>
        /// <param name="enableTimeSync">   When turning on or off sensing, to enable time sync </param>
        /// 
        /// <returns>   The summit error code (which could be no error). </returns>
        ///-------------------------------------------------------------------------------------------------
        public static APIReturnInfo fastImpedanceTest(SummitSystem theSummit, bool enableTimeSync)
        {

            // Sensing must be turned off before a lead integrtiy test can be performed
            SensingState theSensingState;
            theSummit.ReadSensingState(out theSensingState);
            if (theSensingState.State != SenseStates.None)
            {
                APIReturnInfo commandInfo = theSummit.WriteSensingDisableStreams(true, true, true, false, false, true, enableTimeSync, false);
                if (commandInfo.RejectCode != 0)
                {
                    // Failed to turn off sensing!
                    Console.WriteLine("Failed to turn off sensing for impedance test.");
                    return commandInfo;
                }

            }

            Console.WriteLine("Running impedance test...");

            //get electrode pairs
            List<double> thisElecImpedances = new List<double>(); //list to save impedances to
            List<Tuple<byte, byte>> elecPairs = new List<Tuple<byte, byte>>(); //list of pairs to try for this electrode
            StreamingThread.MyRCSMsg.Payload deviceInfo;
            int parseErrorCode;
            APIReturnInfo testReturnInfo = QueryDeviceStatus(theSummit, out deviceInfo, out parseErrorCode);

            //add stim pairs
            int nStimGroups = deviceInfo.stim_config.anodes.Count();
            for (int iGroup = 0; iGroup < nStimGroups; iGroup++)
            {

                int nStimPairs = deviceInfo.stim_config.anodes[iGroup].Count();

                for (int iPair = 0; iPair < nStimPairs; iPair++)
                {
                    elecPairs.Add(new Tuple<byte, byte>((byte)deviceInfo.stim_config.anodes[iGroup][iPair], (byte)deviceInfo.stim_config.cathodes[iGroup][iPair]));
                }

            }

            //add sense pairs
            int nSensePairs = deviceInfo.sense_config.anodes.Count();
            for (int iPair = 0; iPair < nSensePairs; iPair++)
            {
                elecPairs.Add(new Tuple<byte, byte>((byte)deviceInfo.sense_config.anodes[iPair], (byte)deviceInfo.sense_config.cathodes[iPair]));
            }

            //now divide pairs into two groups if there's more than 16
            List<List<Tuple<byte, byte>>> parsedElecPairs = new List<List<Tuple<byte, byte>>>();
            if (elecPairs.Count() > 16)
            {
                //do first 16 pairs
                parsedElecPairs.Add(elecPairs.GetRange(0, 16));

                //then do the rest
                int nRemainingPairs = elecPairs.Count() - 16;
                parsedElecPairs.Add(elecPairs.GetRange(16, nRemainingPairs));
            }
            else
            {
                parsedElecPairs.Add(elecPairs);
            }

            //get the impedances using summit function
            LeadIntegrityTestResult impedanceReadings;
            for (int iCall = 0; iCall < parsedElecPairs.Count(); iCall++)
            {
                testReturnInfo = theSummit.LeadIntegrityTest(parsedElecPairs[iCall], out impedanceReadings);
            }

            //turn sensing back on if it was turned off
            if (theSensingState.State != SenseStates.None)
            {
                APIReturnInfo commandInfo = theSummit.WriteSensingState(SenseStates.LfpSense | SenseStates.Fft | SenseStates.Power, 0x00);
                commandInfo = theSummit.WriteSensingEnableStreams(true, true, true, false, false, true, enableTimeSync, false);
            }


            //finish
            Console.WriteLine("Finished impedance test");
            return testReturnInfo;

        }
        
        //


        }

}
