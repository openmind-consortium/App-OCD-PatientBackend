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
        public static bool SummitConnect(SummitManager theSummitManager, ref SummitSystem theSummit)
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
                ManagerConnectStatus connectReturn = theSummitManager.CreateSummit(out tempSummit, theSummitManager.GetKnownTelemetry()[i], 
                    ctmBeepEnables: (CtmBeepEnables.NoDeviceDiscovered | CtmBeepEnables.GeneralAlert | CtmBeepEnables.TelMLost));

                // Write out the result
                Console.WriteLine("Create Summit Result: " + connectReturn.ToString());

                // Break if it failed successful
                if (connectReturn == ManagerConnectStatus.Success)
                {
                    break;
                }
            }

            // Make sure telemetry was connected to, if not fail
            if (tempSummit == null)
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
                    tempSummit.OlympusDiscovery(out discoveredDevices);
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
                    connectReturn = tempSummit.StartInsSession(discoveredDevices[0], out theWarnings, true);
                    i++;
                } while (theWarnings.HasFlag(ConnectReturn.InitializationError));

                // Write out the number of times a StartInsSession was attempted with initialization errors
                Console.WriteLine("Initialization Error Count: " + i.ToString());

                // Write out the final result of the example
                if (connectReturn.RejectCode != 0)
                {
                    Console.WriteLine("Summit Initialization: INS failed to connect");
                    theSummitManager.DisposeSummit(tempSummit);
                    return false;
                }
                else
                {
                    // Write out the warnings if they exist
                    Console.WriteLine("Summit Initialization: INS connected, warnings: " + theWarnings.ToString());
                    theSummit = tempSummit;

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
        /// <summary>   Function to set stim group configuations from the JSON parameters (using future
        ///             authenticated commands)
        ///             </summary>
        ///
        /// <param name="parameters">       . </param>
        /// <param name="stimType">         Type of the stim. </param>
        /// <param name="groupIndex">       Zero-based index of the group. </param>
        /// <param name="groupConfig">      [out] The group configuration. </param>
        /// <param name="groupNum">         [out] The group number. </param>
        /// <param name="groupNumActive">   [out] The group number active. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void ConfigureStimGroup(INSParameters parameters, string stimType, int? groupIndex, out TherapyGroup groupConfig, 
            out GroupNumber groupNum, out ActiveGroup groupNumActive)
        {
            groupConfig = new TherapyGroup();

            //get the parameters
            StimGroupParameters groupParams;
            GetGroupParameters(parameters, stimType, groupIndex, out groupParams);

            groupNum = groupParams.groupNum;
            groupNumActive = groupParams.groupNumActive;

            groupConfig.RampRepeat = groupParams.rampType;
            groupConfig.RampTime = (byte)groupParams.rampTime;
            groupConfig.AmplitudeResolution0_2mA = 0x00; //default
            groupConfig.PulseWidthLowerLimit = (byte)groupParams.pwLower;
            groupConfig.PulseWidthUpperLimit = (byte)groupParams.pwUpper;
            groupConfig.RatePeriod = (ushort)groupParams.ratePeriod;
            groupConfig.RatePeriodLowerLimit = (ushort)groupParams.ratePeriodLower;
            groupConfig.RatePeriodUpperLimit = (ushort)groupParams.ratePeriodUpper;

        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Helper function for ConfigureStimGroup() to get the group parameters with error
        ///             checking.
        ///             </summary>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="parameters">   . </param>
        /// <param name="stimType">     Type of the stim. </param>
        /// <param name="groupIndex">   Zero-based index of the group. </param>
        /// <param name="groupParams">  [out] Options for controlling the group. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void GetGroupParameters(INSParameters parameters, string stimType, int? groupIndex, out StimGroupParameters groupParams)
        {
            //get value from parameters

            //first group number
            int paramGroupNum = GetParamValueFromPossibleArray(parameters, stimType + ".GroupNumber", typeof(int), groupIndex);

            //parse into the Medtronic Enum definition
            try
            {
                groupParams.groupNum = (GroupNumber)Enum.Parse(typeof(GroupNumber), "Group" + paramGroupNum);
                groupParams.groupNumActive = (ActiveGroup)Enum.Parse(typeof(ActiveGroup), "Group" + paramGroupNum);
            }
            catch
            {
                throw new Exception(String.Format("Chosen group number: {0}, isn't a valid selection, check JSON file specifications!",
                    paramGroupNum));
            }

            //next get ramp type
            string paramRampType = GetParamValueFromPossibleArray(parameters, stimType + ".RampingType", typeof(string), groupIndex);
            try
            {
                groupParams.rampType = (RampingTypes)Enum.Parse(typeof(RampingTypes), paramRampType);
            }
            catch
            {
                throw new Exception(String.Format("Chosen stim ramping type: {0}, isn't a valid selection, check JSON file specifications!",
                    paramRampType));
            }

            //get ramp time
            //TODO: Check units
            groupParams.rampTime = GetParamValueFromPossibleArray(parameters, stimType + ".RampTimeMilliSeconds", typeof(int), groupIndex);

            //for stim sweep, don't get pulse width or rate limits
            if (stimType == "StimSweep")
            {
                groupParams.pwLower = 0;
                groupParams.pwUpper = 0;
                groupParams.ratePeriod = 0;
                groupParams.ratePeriodLower = 0;
                groupParams.ratePeriodUpper = 0;
                return;
            }


            //get pulse width lower limit
            int pwLL = GetParamValueFromPossibleArray(parameters, stimType + ".PulseWidthLowerLimitMicroSeconds", typeof(int), groupIndex);
            ConvertPrecisionOf10(pwLL, out groupParams.pwLower, "Pulse Width", "For pulse width lower limit at " + stimType);

            //get pulse width upper limit
            int pwUL = GetParamValueFromPossibleArray(parameters, stimType + ".PulseWidthUpperLimitMicroSeconds", typeof(int), groupIndex);
            ConvertPrecisionOf10(pwUL, out groupParams.pwUpper, "Pulse Width", "For pulse width upper limit at " + stimType);

            //get rate period, the stim pulse period (convert to 10us since that's the units for TherapyGroup.RatePeriod)
            int rate = GetParamValueFromPossibleArray(parameters, stimType + ".RatePeriodMicroSeconds", typeof(int), groupIndex);
            ConvertPrecisionOf10(rate, out groupParams.ratePeriod, "Rate Period", "For stim rate period at " + stimType);

            //get rate period upper limit (convert to 10us)
            int rateLL = GetParamValueFromPossibleArray(parameters, stimType + ".RatePeriodLowerLimitMicroSeconds", typeof(int), groupIndex);
            ConvertPrecisionOf10(rateLL, out groupParams.ratePeriodLower, "Rate Period", "For rate period upper limit at " + stimType);

            //get rate period lower limit (convert to 10us)
            int rateUL = GetParamValueFromPossibleArray(parameters, stimType + ".RatePeriodUpperLimitMicroSeconds", typeof(int), groupIndex);
            ConvertPrecisionOf10(rateUL, out groupParams.ratePeriodUpper, "Rate Period", "For rate period lower limit at " + stimType);
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Function to make Power band configurations from the JSON parameters. </summary>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="parameters">           . </param>
        /// <param name="stimType">             Type of the stim. </param>
        /// <param name="programIndex">         Zero-based index of the program. </param>
        /// <param name="programConfig">        [out] The program configuration. </param>
        /// <param name="amplitudeLimitLower">  [out] The amplitude limit lower. </param>
        /// <param name="amplitudeLimitUpper">  [out] The amplitude limit upper. </param>
        /// <param name="definedParams">        (Optional) Options for controlling the defined. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void ConfigureStimProgram(INSParameters parameters, string stimType, int? programIndex, out TherapyProgram programConfig,  out int amplitudeLimitLower,
            out int amplitudeLimitUpper, StimProgramParameters? definedParams = null)
        {
            programConfig = new TherapyProgram();
            StimProgramParameters programParams;

            if (definedParams == null)
            {
                //get the parameters if they aren't supplied
                GetProgramParameters(parameters, stimType, programIndex, out programParams);
            }
            else
            {
                programParams = definedParams.Value;
            }

            //output the amp limits
            amplitudeLimitLower = programParams.ampLower;
            amplitudeLimitUpper = programParams.ampUpper;

            //first check that anode and cathode are on the same bore!
            if (programParams.anode != 16 && programParams.cathode != 16)
            {
                if (programParams.anode <= 7 && programParams.cathode > 7 || programParams.cathode <= 7 && programParams.anode > 7)
                {
                    throw new Exception(String.Format("Cannot stim across bores! Anode was set as {0}, cathode as {1}",
                        programParams.anode, programParams.cathode));
                }
            }
            if (programParams.anode == programParams.cathode)
            {
                throw new Exception(String.Format("Cannot use the same electrode as both anode and cathode! In {0}", stimType));
            }

            //make electrode assignments
            Electrode cathode = new Electrode();
            cathode.ElectrodeType = ElectrodeTypes.Cathode;
            cathode.IsOff = false;
            cathode.Value = 63; //default should be 63 for electrodes

            Electrode anode = new Electrode();
            anode.ElectrodeType = ElectrodeTypes.Anode;
            anode.IsOff = false;
            anode.Value = 63;
            Electrode none = new Electrode();

            none.IsOff = true;

            List<Electrode> electrodeAssignments =
                new List<Electrode>{none, none, none, none,
                                    none, none, none, none,
                                    none, none, none, none,
                                    none, none, none, none, none};

            electrodeAssignments[programParams.anode] = anode;
            electrodeAssignments[programParams.cathode] = cathode;

            //add electrodes to therapy program
            programConfig.Electrodes = new TherapyElectrodes(
                electrodeAssignments[0],
                electrodeAssignments[1],
                electrodeAssignments[2],
                electrodeAssignments[3],
                electrodeAssignments[4],
                electrodeAssignments[5],
                electrodeAssignments[6],
                electrodeAssignments[7],
                electrodeAssignments[8],
                electrodeAssignments[9],
                electrodeAssignments[10],
                electrodeAssignments[11],
                electrodeAssignments[12],
                electrodeAssignments[13],
                electrodeAssignments[14],
                electrodeAssignments[15],
                electrodeAssignments[16]
                );

            //assign parameters to program
            programConfig.Amplitude = (byte)programParams.amplitude;
            programConfig.PulseWidth = (byte)programParams.pulseWidth;

        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Helper function for ConfigureStimProgram() to get the program parameters with
        ///             error checking.
        ///             </summary>
        ///
        /// <param name="parameters">       . </param>
        /// <param name="stimType">         Type of the stim. </param>
        /// <param name="programIndex">     Zero-based index of the program. </param>
        /// <param name="programParams">    [out] Options for controlling the program. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void GetProgramParameters(INSParameters parameters, string stimType, int? programIndex, out StimProgramParameters programParams)
        {
            //get value from parameters

            //first, get then anode electrode number
            programParams.anode = GetParamValueFromPossibleArray(parameters, stimType + ".Anode", typeof(int), programIndex);

            //next get cathode electrode number
            programParams.cathode = GetParamValueFromPossibleArray(parameters, stimType + ".Cathode", typeof(int), programIndex);

            //don't need the rest when getting stim sweep
            if (stimType == "StimSweep")
            {
                programParams.amplitude = 0;
                programParams.pulseWidth = 0;
                programParams.ampLower = 0;
                programParams.ampUpper = 0;
                return;
            }

            //get the stim pulse amplitude, convert to 0.1 mA's since that's what is used in the API
            var paramAmps = GetParamValueFromPossibleArray(parameters, stimType + ".StimAmplitudesMilliAmps", typeof(double), programIndex);
            ConvertAmplitude(paramAmps, out programParams.amplitude, "For stim amplitude at " + stimType + ", program " + ((programIndex == null) ? "" : programIndex.Value.ToString()));

            //get the stim pulse widths, convert to 10's of microseconds since thats what is used in the API
            var paramPWs = GetParamValueFromPossibleArray(parameters, stimType + ".PulseWidthsMicroSeconds", typeof(int), programIndex);
            ConvertPrecisionOf10(paramPWs, out programParams.pulseWidth, "Pulse Width", "For stim pulse width at " + stimType + ", program " + ((programIndex == null) ? "" : programIndex.Value.ToString()));

            //get stim amplitude lower limit, convert to 0.1 mA's since that's what is used in the API
            var paramAmpLowerLimits = GetParamValueFromPossibleArray(parameters, stimType + ".AmplitudeLimitsLowerMilliAmps", typeof(double), programIndex);
            ConvertAmplitude(paramAmpLowerLimits, out programParams.ampLower, "For amplitude lower limit at " + stimType + ", program " + ((programIndex == null) ? "" : programIndex.Value.ToString()));

            //get stim amplitude upper limit, convert to 0.1 mA's since that's what is used in the API
            var paramAmpUpperLimits = GetParamValueFromPossibleArray(parameters, stimType + ".AmplitudeLimitsUpperMilliAmps", typeof(double), programIndex);
            ConvertAmplitude(paramAmpUpperLimits, out programParams.ampUpper, "For amplitude upper limit at " + stimType + ", program " + ((programIndex == null) ? "" : programIndex.Value.ToString()));
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Function to configure stimulation sweep from JSON parameters. </summary>
        ///
        /// <param name="parameters">               . </param>
        /// <param name="summit">                   The summit. </param>
        /// <param name="sweepType">                Type of the sweep. </param>
        /// <param name="sweeper">                  [out] The sweeper. </param>
        /// <param name="stimSweepGroupConfig">     [in,out] The stim sweep group configuration. </param>
        /// <param name="stimSweepProgramParams">   [in,out] Options for controlling the stim sweep
        ///                                         program. </param>
        /// <param name="configParameters">         [in,out] Options for controlling the configuration. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void configureStimSweep(INSParameters parameters, SummitSystem summit, string sweepType, out StimSweeper sweeper, 
            ref TherapyGroup stimSweepGroupConfig, ref StimProgramParameters stimSweepProgramParams, ref SweepConfigParameters configParameters)
        {
            List<APIReturnInfo> initReturnInfo = new List<APIReturnInfo>();

            //get stim group parameters
            StimGroupParameters groupParams;
            GetGroupParameters(parameters, "StimSweep", null, out groupParams);

            //return group nums
            if (configParameters.startingRatePeriod == null)
            {
                configParameters.stimSweepGroupNum = groupParams.groupNum;
                configParameters.stimSweepGroupActive = groupParams.groupNumActive;
            }

            //get Sweep parameters
            int ampLowerLimit, ampUpperLimit, pulseWidthLowerLimit, pulseWidthUpperLimit;
            ushort ratePeriodLowerLimit, ratePeriodUpperLimit;

            StimSweeper.SweepParameters sweepParams;
            GetStimSweepParameters(parameters, sweepType, out sweepParams);

            //get the upper and lower limits of the amplitudes (convert to 0.1mAs)
            double ampUpper = sweepParams.ampValues.Max();
            ConvertAmplitude(ampUpper, out ampUpperLimit, "For getting Stim Sweep amplitude upper limit at " + sweepType);
            double ampLower = sweepParams.ampValues.Min();
            ConvertAmplitude(ampLower, out ampLowerLimit, "For getting Stim Sweep amplitude lower limit at " + sweepType);

            //get the upper and lower limits of the pulse widths (convert to 10's of us)
            int pulseWidthUpper = sweepParams.pulseWidthValues.Max();
            ConvertPrecisionOf10(pulseWidthUpper, out pulseWidthUpperLimit, "For getting Stim Sweep pulse width upper limit at " + sweepType);
            int pulseWidthLower = sweepParams.pulseWidthValues.Min();
            ConvertPrecisionOf10(pulseWidthLower, out pulseWidthLowerLimit, "For getting Stim Sweep pulse width lower limit at " + sweepType);

            //lower and upper limits for rate periods
            double freqUpper = sweepParams.freqValues.Max();
            ConvertHzToRatePeriod(freqUpper, out ratePeriodLowerLimit, "For getting Stim Sweep amplitude upper limit at Stim Bursts");
            double freqLower = sweepParams.freqValues.Min();
            ConvertHzToRatePeriod(freqLower, out ratePeriodUpperLimit, "For getting Stim Sweep amplitude lower limit at Stim Bursts");


            //fill in config values for configParameters
            if (configParameters.startingRatePeriod == null)
            {
                //if the config parameters haven't been defined yet, use these parameters as the starting values, and the upper and lower limits
                ushort startRatePeriod;
                int startPulseWidth, startAmplitude = 0; //start amp should be 0 otherwise we're stiming when we're adjusting the pw and freq
                ConvertHzToRatePeriod(sweepParams.freqValues[0], out startRatePeriod, "For getting single pulse stim sweep frequency");
                ConvertPrecisionOf10(sweepParams.pulseWidthValues[0], out startPulseWidth, "For getting single pulse stim sweep starting pulse width");
                configParameters.startingAmp = startAmplitude;
                configParameters.startingPulseWidth = startPulseWidth;
                configParameters.startingRatePeriod = startRatePeriod;
                configParameters.ampLowerLimit = 0; //lower limit should also be 0
                configParameters.ampUpperLimit = ampUpperLimit;
                configParameters.pulseWidthLowerLimit = pulseWidthLowerLimit;
                configParameters.pulseWidthUpperLimit = pulseWidthUpperLimit;
                configParameters.ratePeriodLowerLimit = ratePeriodLowerLimit;
                configParameters.ratePeriodUpperLimit = ratePeriodUpperLimit;
            }
            else
            {
                //update the lower and upper limits if these new ones are wider
                configParameters.ampUpperLimit = Math.Max(ampUpperLimit, configParameters.ampUpperLimit);
                configParameters.pulseWidthLowerLimit = Math.Min(pulseWidthLowerLimit, configParameters.pulseWidthLowerLimit);
                configParameters.pulseWidthUpperLimit = Math.Min(pulseWidthUpperLimit, configParameters.pulseWidthUpperLimit);
                configParameters.ratePeriodLowerLimit = Math.Min(ratePeriodLowerLimit, configParameters.ratePeriodLowerLimit);
                configParameters.ratePeriodUpperLimit = Math.Max(ratePeriodUpperLimit, configParameters.ratePeriodUpperLimit);
            }


            //configure group
            stimSweepGroupConfig.RampRepeat = groupParams.rampType;
            stimSweepGroupConfig.RampTime = (byte)groupParams.rampTime;
            stimSweepGroupConfig.AmplitudeResolution0_2mA = 0x00; //default


            //configure stim program parameters
            stimSweepProgramParams.anode = parameters.GetParam("StimSweep.Anode", typeof(int));
            stimSweepProgramParams.cathode = parameters.GetParam("StimSweep.Cathode", typeof(int));

            string sweepStopButton = parameters.GetParam("StimSweep.StopButton", typeof(string));

            //configure single pulse stim sweeper
            sweeper = new StimSweeper(sweepParams, summit, groupParams.groupNum, sweepStopButton);
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>   Helper function for configureStimSweep() to get stimulation sweep parameters from
        ///             JSON parameters.
        ///             </summary>
        ///
        /// <exception cref="Exception">    Thrown when an exception error condition occurs. </exception>
        ///
        /// <param name="parameters">   . </param>
        /// <param name="sweepType">    Type of the sweep. </param>
        /// <param name="sweepParams">  [out] Options for controlling the sweep. </param>
        ///-------------------------------------------------------------------------------------------------
        public static void GetStimSweepParameters(INSParameters parameters, string sweepType, out StimSweeper.SweepParameters sweepParams)
        {

            //set up amplitude, pulse width, and frequency parameters (don't get freq if single pulse sweep)
            List<string> parameterNames;
            if (sweepType == "StimBurstSweep")
            {
                parameterNames = new List<string>() { "AmplitudeMilliAmps", "PulseWidthMicroSeconds", "FrequencyHz" };
            }
            else
            {
                parameterNames = new List<string>() { "AmplitudeMilliAmps", "PulseWidthMicroSeconds" };
            }

            //initialize return variables            
            sweepParams.paramNumValues = new List<int>() { 0, 0, 1 };
            sweepParams.paramOrder = new List<int>() { 0, 0, 2 };
            sweepParams.paramPauseDurationMilliSeconds = new List<int>() { 0, 0, 0};

            sweepParams.ampOrder = 0;
            sweepParams.ampValues = new List<double>();

            sweepParams.pulseWidthOrder = 0;
            sweepParams.pulseWidthValues = new List<int>();

            sweepParams.freqOrder = 2; //when using single pulse sweep, freq order is 2
            sweepParams.freqValues = new List<double>() { 2 }; //since we're doing single pulse, use frequency of 1.6Hz, TODO: verify with Ben if this is about the lowest it can go)
            

            //for amp, pulsewidth, and frequency, go through and get the values from the JSON parameters
            for (int iParam = 0; iParam < parameterNames.Count; iParam++)
            {
                //get num values
                int numValues = parameters.GetParam("StimSweep." + sweepType + "." + parameterNames[iParam] + ".nValues", typeof(int));
                sweepParams.paramNumValues[iParam] = numValues;

                //get order
                int order = parameters.GetParam("StimSweep." + sweepType + "." + parameterNames[iParam] + ".SweepOrder", typeof(int));
                sweepParams.paramOrder[iParam] = order;

                //get sweep values
                var customValues = parameters.GetParam("StimSweep." + sweepType + "." + parameterNames[iParam] + ".CustomValues", typeof(double));
                List<double> sweepValues = new List<double>();

                //if there are not custom values, build the values
                if (customValues.Count==0)
                {
                    sweepValues.Add(parameters.GetParam("StimSweep." + sweepType + "." + parameterNames[iParam] + ".StartValue", typeof(double)));
                    double increValue = parameters.GetParam("StimSweep." + sweepType + "." + parameterNames[iParam] + ".IncrementValue", typeof(double));
                    for (int iValue = 1; iValue < numValues; iValue++)
                    {
                        sweepValues.Add(Math.Round(sweepValues[iValue - 1] + increValue,3));
                    }
                }
                else
                {
                    foreach (var paramValue in customValues)
                    {
                        sweepValues.Add(paramValue);
                    }
                }

                //get pause duration
                sweepParams.paramPauseDurationMilliSeconds[iParam] = parameters.GetParam("StimSweep." + sweepType + "." + parameterNames[iParam] + ".DurationBetweenValuesMilliSeconds", typeof(int));

                //assign values
                switch (iParam)
                {
                    case 0:
                        //in parameterNames above, assigend amp first
                        sweepParams.ampOrder = order;
                        sweepParams.ampValues = sweepValues;
                        break;

                    case 1:
                        //next was pulse width
                        sweepParams.pulseWidthOrder = order;
                        sweepParams.pulseWidthValues = sweepValues.ConvertAll(x => (int)x);
                        break;

                    case 2:
                        //last was frequency
                        sweepParams.freqOrder = order;
                        sweepParams.freqValues = sweepValues;
                        break;
                }

            }

            //check that the orders are mutually exclusive            
            if (sweepParams.ampOrder == sweepParams.pulseWidthOrder || sweepParams.pulseWidthOrder == sweepParams.freqOrder)
            {
                throw new Exception("AmplitudeMilliAmps, PulseWidthMicroSeconds, and FrequencyHz cannot have the same sweep order!");
            }


            //finally get the duration of each sweep permutation
            if (sweepType == "StimBurstSweep")
            {
                sweepParams.permutationDuration = parameters.GetParam("StimSweep.StimBurstSweep.DurationPerPermutationMilliseconds", typeof(int));
            }
            else
            {
                //single pulses at 1.6 Hz, so each pulse is about 625ms. Use 600 for some wiggle room
                sweepParams.permutationDuration = parameters.GetParam("StimSweep.SinglePulseSweep.numPulses", typeof(int)) * 600;

                //also set the frequency num values (just 1), order (two), and pause duration (zero) for single pulse sweep
                sweepParams.paramNumValues[2] = 1;
                sweepParams.paramOrder[2] = 2;
                sweepParams.paramPauseDurationMilliSeconds[2] = 0;
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
        public static bool ConvertEnumsToValues(string enumName, string enumType, out float value)
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

                    if (enumName == "floating")
                    {
                        value = 16;
                        return true;
                    }
                    else
                    {
                        if (!float.TryParse(enumName.Substring(3), out value))
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

                    if (!float.TryParse(enumName.Substring(3, nChars - 5), out value))
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

                    if (!float.TryParse(enumName.Substring(3, nChars - 5).Replace('_', '.'), out value))
                    {
                        return false;
                    }
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
                    if (!float.TryParse(enumName.Substring(6, nChars - 8), out value))
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

                    if (!float.TryParse(enumName.Substring(4, nChars - 4), out value))
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

                    if (!float.TryParse(enumName.Substring(5), out value))
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
        /// 
        /// <returns>   True if it succeeds, false if it fails. </returns>
        ///-------------------------------------------------------------------------------------------------
        public static bool QueryDeviceStatus(SummitSystem theSummit, out StreamingThread.MyRCSMsg.Payload payload)
        {
            payload = new StreamingThread.MyRCSMsg.Payload();

            //run queries using the summit API functions
            APIReturnInfo commandInfo = new APIReturnInfo();

            //first get sense info
            SensingConfiguration sensingConfig = new SensingConfiguration();
            commandInfo = theSummit.ReadSensingSettings(out sensingConfig);
            if (commandInfo.RejectCode != 0)
            {
                return false;
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

                float anodeChan, cathodeChan;

                enumName = sensingConfig.TimeDomainChannels[iChan].PlusInput.ToString();
                if (!ConvertEnumsToValues(enumName, "TdMuxInputs", out anodeChan))
                {
                    return false;
                }
                if (anodeChan != 16)
                {
                    anodeChan = anodeChan + boreOffset;
                }

                enumName = sensingConfig.TimeDomainChannels[iChan].MinusInput.ToString();
                if(!ConvertEnumsToValues(enumName, "TdMuxInputs", out cathodeChan))
                {
                    return false;
                }
                if (cathodeChan != 16)
                {
                    cathodeChan = cathodeChan + boreOffset;
                }

                payload.sense_config.anodes.Add(Convert.ToUInt16(anodeChan));
                payload.sense_config.cathodes.Add(Convert.ToUInt16(cathodeChan));

                //next get the filter values for each channel
                float lpf1Value, lpf2Value, hpfValue;

                enumName = sensingConfig.TimeDomainChannels[iChan].Lpf1.ToString();
                if(!ConvertEnumsToValues(enumName, "TdLpfStage1", out lpf1Value))
                {
                    return false;
                }
                payload.sense_config.lowpass_filter1.Add(Convert.ToUInt16(lpf1Value));

                enumName = sensingConfig.TimeDomainChannels[iChan].Lpf2.ToString();
                if(!ConvertEnumsToValues(enumName, "TdLpfStage2", out lpf2Value))
                {
                    return false;
                }
                payload.sense_config.lowpass_filter2.Add(Convert.ToUInt16(lpf2Value));

                enumName = sensingConfig.TimeDomainChannels[iChan].Hpf.ToString();
                if(!ConvertEnumsToValues(enumName, "TdHpfs", out hpfValue))
                {
                    return false;
                }
                payload.sense_config.highpass_filter.Add(hpfValue);
                
                //then the sampling rates
                float samplingRate;
                enumName = sensingConfig.TimeDomainChannels[iChan].SampleRate.ToString();
                if(!ConvertEnumsToValues(enumName, "TdSampleRates", out samplingRate))
                {
                    return false;
                }
                payload.sense_config.sampling_rates.Add(Convert.ToUInt16(samplingRate));
            }

            //fft config
            float fftSize, fftWindowLoad, fftStreamSize, fftStreamOffset;

            enumName = sensingConfig.FftConfig.Size.ToString();
            if (!ConvertEnumsToValues(enumName, "FftSizes", out fftSize))
            {
                return false;
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
                return false;
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
                    return false;
                }

                payload.sense_config.powerband1_enabled.Add(sensingConfig.BandEnable.HasFlag(enabledEnum1));
                payload.sense_config.powerband2_enabled.Add(sensingConfig.BandEnable.HasFlag(enabledEnum2));
            }

            SensingState sensingState = new SensingState();
            commandInfo = theSummit.ReadSensingState(out sensingState);
            commandInfo = theSummit.ReadSensingStreamState(out StreamState streamState);
            if (commandInfo.RejectCode != 0)
            {
                return false;
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
                return false;
            }

            //the current active group
            enumName = insGeneralInfo.TherapyStatusData.ActiveGroup.ToString();
            float currentGroup;
            if (!ConvertEnumsToValues(enumName, "GroupNumber", out currentGroup))
            {
                return false;
            }
            payload.stim_config.current_group = Convert.ToUInt16(currentGroup);

            //whether stim is currently on or not
            enumName = insGeneralInfo.TherapyStatusData.TherapyStatus.ToString();
            float stimOn;
            if (!ConvertEnumsToValues(enumName, "InterrogateTherapyStatusTypes", out stimOn))
            {
                return false;
            }
            payload.stim_on = (stimOn == 1);

            //go through each therapy group
            TherapyGroup groupSettings = new TherapyGroup();
            AmplitudeLimits ampLimits = new AmplitudeLimits();
            
            foreach (GroupNumber iGroup in Enum.GetValues(typeof(GroupNumber)))
            {
                float thisGroupNum;
                if (!ConvertEnumsToValues(iGroup.ToString(), "GroupNumber", out thisGroupNum))
                {
                    return false;
                }
                int iGroupInd = Convert.ToInt16(thisGroupNum);

                commandInfo = theSummit.ReadStimGroup(iGroup, out groupSettings);
                if (commandInfo.RejectCode != 0)
                {
                    return false;
                }

                commandInfo = theSummit.ReadStimAmplitudeLimits(iGroup, out ampLimits);
                if (commandInfo.RejectCode != 0)
                {
                    return false;
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
                    float enabled;
                    if (!ConvertEnumsToValues(enumName, "ProgramEnables", out enabled))
                    {
                        return false;
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
                                return false;
                            }
                            anode = iElec;
                        }
                        if (!electrodesInfo[iElec].IsOff && electrodesInfo[iElec].ElectrodeType == ElectrodeTypes.Cathode)
                        {
                            if (cathode != -1)
                            {
                                //an cathode was already found, so we have two cathode which shouldn't happen
                                return false;
                            }
                            cathode = iElec;
                        }
                    }

                    //if either anode or cathode hasn't been found, then something is wrong, otherwise, just add to the list
                    if (anode == -1 || cathode == -1)
                    {
                        return false;
                    }
                    payload.stim_config.anodes[iGroupInd].Add(Convert.ToUInt16(anode));
                    payload.stim_config.cathodes[iGroupInd].Add(Convert.ToUInt16(cathode));

                    //add the rest of the information
                    payload.stim_config.current_pulsewidth[iGroupInd].Add(Convert.ToUInt16(groupSettings.Programs[iProg].PulseWidthInMicroseconds));
                    payload.stim_config.current_amplitude[iGroupInd].Add(groupSettings.Programs[iProg].AmplitudeInMilliamps);
                    
                    enumName = groupSettings.Programs[iProg].MiscSettings.ActiveRechargeRatio.ToString();
                    if (!ConvertEnumsToValues(enumName, "ActiveRechargeRatios", out enabled))
                    {
                        return false;
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

            return true;
        }




        //

    }

}
