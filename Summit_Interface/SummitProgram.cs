using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Threading;
using System.Windows.Forms;

using Medtronic.SummitAPI.Classes;
using Medtronic.SummitAPI.Events;
using Medtronic.TelemetryM;
using Medtronic.NeuroStim.Olympus.DataTypes.Core;
using Medtronic.NeuroStim.Olympus.DataTypes.Sensing;
using Medtronic.NeuroStim.Olympus.DataTypes.Measurement;
using Medtronic.NeuroStim.Olympus.Commands;
using Medtronic.NeuroStim.Olympus.DataTypes.PowerManagement;
using Medtronic.NeuroStim.Olympus.DataTypes.Therapy;
using Medtronic.NeuroStim.Olympus.DataTypes.DeviceManagement;

using NetMQ;
using NetMQ.Sockets;

namespace Summit_Interface
{
    class SummitProgram
    {
        //Session experimental parameters
        static INSParameters parameters;

        //Buffers for storing data from the INS
        static INSBuffer m_TDBuffer; //time domain voltages
        static INSBuffer m_FFTBuffer; //Frequency domain
        static INSBuffer m_BPBuffer; //Band power
        static INSBuffer m_dataSavingBuffer; //saving to file buffer

        //Summit API object
        static SummitSystem m_summit;

        //sweeper class for sweeping through stim parameters
        static StimSweeper m_singlePulseSweeper;
        static StimSweeper m_stimBurstSweeper;

        //files for saving data, debugging, and logging
        static string m_dataFileName;
        static ThreadsafeFileStream m_timingLogFile; //key: 0 for INS packet received, 1 for OpenEphys sense request, 2 for Openephys stim received, 5 for single pulse stim incr, 6 for 20Hz stim incr, 7 for 80Hz stim incr, space delim,
        static ThreadsafeFileStream m_debugFile;
        static bool notifyCTM;
        static bool notifyOpenEphys;
        static bool interp;
        //initialization variables for packet handling threads
        static int m_nTDChans = 0;
        static int m_TDPacketCount = 0;
        static bool m_firstPacket = true;
        static ushort m_prevPacketTime = 0; //TODO: make thread safe
        static long m_prevPacketEstTime = 0; //TODO: make thread safe
        static int m_prevPacketNSamples = 0; //TODO: make thread safe
        static int m_prevPacketNum = 0; //TODO: make thread safe
        static double[] m_prevLastValues = new double[0]; //default have to have something, but it's a dont-care
        static TdSampleRates m_samplingRate;
        static bool m_singlePulseSweeping;
        static bool m_stimBurstSweeping;

        //Main program
        [STAThread]
        static void Main(string[] args)
        {
            ////Initiation===============================================================
            // Tell user this code is not for human use
            Console.WriteLine("Starting Summit Interface Code for RC+S");
            Console.WriteLine("This code is not for human use, either close program window or proceed by pressing a key");
            Console.ReadKey();
            Console.WriteLine("");

            //First, load in parameters from JSON file
            OpenFileDialog parametersFileDialog = new OpenFileDialog();
            parametersFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
            parametersFileDialog.FilterIndex = 0;
            parametersFileDialog.RestoreDirectory = true;
            parametersFileDialog.Multiselect = false;
            parametersFileDialog.Title = "Select Summit parameters JSON file";
            string parametersFileName;
            if (parametersFileDialog.ShowDialog() == DialogResult.OK)
            {
                parametersFileName = parametersFileDialog.FileName;
            }
            else
            {
                Console.WriteLine("Error: unable to read parameters file");
                Console.WriteLine("Press a key to close.");
                Console.ReadKey();
                return;
            }

            parameters = new INSParameters(parametersFileName);

            // set up buffers
            int numSenseChans = parameters.GetParam("Sense.nChans", typeof(int));
            int bufferSize = parameters.GetParam("Sense.BufferSize", typeof(int)); // Make sure this is not larger than the AudioSampleBuffer buffer that Open-Ephys uses! (1024 last time I checked)
            m_TDBuffer = new INSBuffer(numSenseChans, bufferSize);
            m_FFTBuffer = new INSBuffer(1, bufferSize);
            m_BPBuffer = new INSBuffer(numSenseChans * 2, bufferSize);
            m_dataSavingBuffer = new INSBuffer(numSenseChans, bufferSize);

            //set up files
            m_dataFileName = parameters.GetParam("Sense.SaveFileName", typeof(string));
            if (m_dataFileName.Length > 4)
            {
                if (m_dataFileName.Substring(m_dataFileName.Length-4,4) == ".txt")
                {
                    m_dataFileName = m_dataFileName.Substring(0, m_dataFileName.Length - 4);
                }
            }
            m_timingLogFile = new ThreadsafeFileStream(m_dataFileName + "-Timing.txt");
            m_debugFile = new ThreadsafeFileStream(m_dataFileName + "-Debug.txt");
            System.IO.StreamWriter m_impedanceFile = new System.IO.StreamWriter(m_dataFileName + "-Impedance.txt");

            //TODO: check if the file exists, and warn user that it will be overwritten if it does


            ////Connect to Device=========================================================
            // Initialize the Summit Interface
            Console.WriteLine();
            Console.WriteLine("Creating Summit Interface...");

            // Create a manager
            SummitManager theSummitManager = new SummitManager("BSI");

            // Connect to CTM and INS
            if (!SummitUtils.SummitConnect(theSummitManager, ref m_summit))
            {
                // Failed to connect. Could error handle and retry. Instead we will close the program.
                theSummitManager.Dispose();

                Console.WriteLine("Press a key to close.");
                Console.ReadKey();
                return;
            }

            
            //check battery level and display
            BatteryStatusResult outputBuffer;
            APIReturnInfo commandInfo = m_summit.ReadBatteryLevel(out outputBuffer);

            TelemetryModuleInfo info;
            m_summit.ReadTelemetryModuleInfo(out info);

            Console.WriteLine();
            Console.WriteLine(String.Format("CTM Battery Level: {0}", info.BatteryLevel));

            // Ensure the command was successful before using the result
            if (commandInfo.RejectCode == 0)
            {
                string batteryLevel = outputBuffer.BatteryLevelPercent.ToString();
                Console.WriteLine("INS Battery Level: " + batteryLevel);
            }
            else
            {
                Console.WriteLine("Unable to read battery level");
            }
            Console.WriteLine();

            //run impedance test
            if (parameters.GetParam("RunImpedanceTest", typeof(bool)))
            {
                // Sensing must be turned off before a lead integrtiy test can be performed
                APIReturnInfo sensingOffStatus = m_summit.WriteSensingState(SenseStates.None, 0);
                Console.WriteLine("Running impedance test...");
                if (sensingOffStatus.RejectCode != 0)
                {
                    // Failed to turn off sensing!
                    Console.WriteLine("Failed to turn off sensing. Press a key to exit.");
                    Console.ReadKey();

                    // Dispose Summit
                    theSummitManager.Dispose();

                    // Exit
                    return;
                }

                //make headers and labels
                m_impedanceFile.Write("Impedance Testing Time: " + String.Format("{0:F}", DateTime.Now) + "\r\n");
                m_impedanceFile.WriteLine();
                string impedHeader="";
                for (int iElec = 0; iElec < 16; iElec++)
                {
                    impedHeader += ("\tChan " + iElec);
                }

                m_impedanceFile.WriteLine(impedHeader);
                Console.WriteLine(impedHeader);

                // Performing impedance reading across all electrodes
                for (int iElec = 1; iElec < 17; iElec++)
                {
                    //write row label
                    if (iElec == 16)
                    {
                        Console.Write("Case");
                        m_impedanceFile.Write("Case");
                    }
                    else
                    {
                        Console.Write(String.Format("Chan {0}",iElec));
                        m_impedanceFile.Write(String.Format("Chan {0}",iElec));
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
                    APIReturnInfo testReturnInfo = m_summit.LeadIntegrityTest(elecPairs, out impedanceReadings);

                    // Make sure returned structure isn't null
                    if (testReturnInfo.RejectCode == 0 && impedanceReadings != null)
                    {
                        //store values
                        thisElecImpedances = impedanceReadings.PairResults.Where(o => o.Info != 0).Select(o => o.Impedance).ToList();

                        //write to console and file
                        thisElecImpedances.ForEach(i => Console.Write("\t{0}", i));
                        thisElecImpedances.ForEach(i => m_impedanceFile.Write("\t{0}", i));
                    }
                    else
                    {
                        //write error message
                        Console.Write("\tError reading impedance values");
                        m_impedanceFile.Write("\tError reading impedance values");
                    }

                    Console.WriteLine();
                    m_impedanceFile.WriteLine();
                }

                //finish
                Console.WriteLine("Finished impedance test");
                m_impedanceFile.Close();
            }

            ////Configure Sensing============================================================
            bool doSensing = parameters.GetParam("Sense.Enabled", typeof(bool));
            TdSampleRates samplingRate = TdSampleRates.Disabled; //default disabled, if we are sensing, we'll set the sampling rate below

            if (doSensing)
            {
                Console.WriteLine();
                Console.WriteLine("Writing sense configuration...");
                SenseStates theStates = SenseStates.LfpSense;
                APIReturnInfo returnInfoBuffer;

                //first turn off sensing
                m_summit.WriteSensingState(SenseStates.None, 0x00);

                //set up time domain channels configurations
                List<TimeDomainChannel> timeDomainChannels;
                List<int?> indexInJSON;
                SummitUtils.ConfigureTimeDomain(parameters, out indexInJSON, out timeDomainChannels, ref samplingRate);
                m_samplingRate = samplingRate;

                //send time domain config to INS
                returnInfoBuffer = m_summit.WriteSensingTimeDomainChannels(timeDomainChannels);
                SensingConfiguration senseInfo;
                returnInfoBuffer = m_summit.ReadSensingSettings(out senseInfo);
                Console.WriteLine("Write TD Config Status: " + returnInfoBuffer.Descriptor);


                // Set up the FFT
                bool FFTEnabled = parameters.GetParam("Sense.FFT.Enabled", typeof(bool));
                SenseTimeDomainChannel FFTChannel = new SenseTimeDomainChannel();

                if (FFTEnabled)
                {
                    theStates = theStates | SenseStates.Fft;

                    //set up configuration
                    FftConfiguration FFTConfig;
                    SummitUtils.ConfigureFFT(parameters, out FFTConfig, out FFTChannel);

                    //send FFT config to INS
                    returnInfoBuffer = m_summit.WriteSensingFftSettings(FFTConfig);
                    Console.WriteLine("Write FFT Config Status: " + returnInfoBuffer.Descriptor);
                }


                // Set up power channels 
                bool powerEnabled;

                //make power channel configurations
                List<PowerChannel> powerChannels;
                BandEnables theBandEnables;
                SummitUtils.ConfigurePower(parameters, indexInJSON, out powerChannels, out theBandEnables, out powerEnabled);

                if (powerEnabled)
                {
                    theStates = theStates | SenseStates.Power;

                    //send configurations to INS
                    returnInfoBuffer = m_summit.WriteSensingPowerChannels(theBandEnables, powerChannels);
                    Console.WriteLine("Write Power Config Status: " + returnInfoBuffer.Descriptor);
                }


                // Set up miscellaneous settings
                MiscellaneousSensing miscsettings = new MiscellaneousSensing();

                //first, streaming frame period
                int paramFrameSize = parameters.GetParam("Sense.PacketPeriod", typeof(int));
                StreamingFrameRate streamingRate;
                try
                {
                    streamingRate = (StreamingFrameRate)Enum.Parse(typeof(StreamingFrameRate), "Frame" + paramFrameSize + "ms");
                }
                catch
                {
                    throw new Exception(String.Format("Packet frame size: {0}, isn't a valid selection, check JSON file specifications!",
                        paramFrameSize));
                }
                miscsettings.StreamingRate = streamingRate;

                //for now, disable loop recording
                miscsettings.LrTriggers = LoopRecordingTriggers.None;


                // Write misc and accelerometer configurations to INS
                returnInfoBuffer = m_summit.WriteSensingMiscSettings(miscsettings);
                Console.WriteLine("Write Misc Config Status: " + returnInfoBuffer.Descriptor);
                returnInfoBuffer = m_summit.WriteSensingAccelSettings(AccelSampleRate.Sample32);
                Console.WriteLine("Write Accel Config Status: " + returnInfoBuffer.Descriptor);

                // turn on sensing components
                m_summit.WriteSensingState(theStates, FFTEnabled ? FFTChannel : 0x00);
                Console.WriteLine("Write Sensing Config Status: " + returnInfoBuffer.Descriptor);

                // Start streaming for time domain, FFT, power, accelerometer, and time-synchronization.
                // Leave streaming of detector events, adaptive stim, and markers disabled
                returnInfoBuffer = m_summit.WriteSensingEnableStreams(true, FFTEnabled, powerEnabled, false, false, true, true, false);

                //initialize streaming threads variables
                m_nTDChans = parameters.GetParam("Sense.nChans", typeof(int));
                m_prevLastValues = new double[m_nTDChans];

                notifyCTM = parameters.GetParam("NotifyCTMPacketsReceived", typeof(bool));
                interp = parameters.GetParam("Sense.InterpolateMissingPackets", typeof(bool));
            }


            ////Configure Manual Stim=========================================================

            bool doManualStim = parameters.GetParam("ManualStim.Enabled", typeof(bool));
            bool useAuthCommands = !parameters.GetParam("RLPStimSetup", typeof(bool));
            List<GroupNumber?> manualGroupNums = new List<GroupNumber?>();
            List<ActiveGroup?> manualGroupActives = new List<ActiveGroup?>();

            List<TherapyProgram> tmpProg = new List<TherapyProgram>();
            List<TherapyGroup> tmpGroup = new List<TherapyGroup>();
            List<List<int>> validPrograms = new List<List<int>>();

            string startManualStim = null, stopManualStim = null;
            string upAmp = null, downAmp = null, upFreq = null, downFreq = null, upPW = null, downPW = null;
            double? changeAmpAmount = null, changeFreqAmount = null;
            double defaultStimAmp=0; //TODO: change this to actual starting stim amp from JSON
            int? changePWAmount = null;
            var manualGroupButtons = parameters.GetParam("ManualStim.GroupButton", typeof(string));
            var manualProgramButtons = parameters.GetParam("ManualStim.ProgramButton", typeof(string));

            if (doManualStim)
            {
                if (useAuthCommands)
                {
                    Console.WriteLine();
                    Console.WriteLine("Configuring manual stim control...");
                    List<APIReturnInfo> initReturnInfo = new List<APIReturnInfo>();
                    initReturnInfo.Add(m_summit.StimChangeTherapyOff(false));
                    Thread.Sleep(1000); // Wait for therapy to turn off

                    //first get number of stim groups
                    int nGroups = parameters.GetParam("ManualStim.nGroups", typeof(int));

                    //loop through each of the groups and configure them
                    for (int iGroup = 0; iGroup < nGroups; iGroup++)
                    {

                        //set up a group and write to INS
                        TherapyGroup groupConfig;
                        GroupNumber tmpManualGroupNum;
                        ActiveGroup tmpManualGroupActive;

                        SummitUtils.ConfigureStimGroup(parameters, "ManualStim", iGroup, out groupConfig, out tmpManualGroupNum, out tmpManualGroupActive);
                        manualGroupNums.Add(tmpManualGroupNum);
                        manualGroupActives.Add(tmpManualGroupActive);
                        tmpGroup.Add(groupConfig);
                        initReturnInfo.Add(m_summit.zAuthStimModifyClearGroup(manualGroupNums[iGroup].Value));
                        initReturnInfo.Add(m_summit.StimChangeActiveGroup(manualGroupActives[iGroup].Value));
                        initReturnInfo.Add(m_summit.StimChangeTherapyOff(false));
                        Thread.Sleep(1000);
                        initReturnInfo.AddRange(m_summit.zAuthStimWriteGroup(manualGroupNums[iGroup].Value, groupConfig));

                        //set up programs
                        //make program
                        TherapyProgram programConfig;
                        int amplitudeLimitLower, amplitudeLimitUpper;
                        SummitUtils.ConfigureStimProgram(parameters, "ManualStim", 0, out programConfig, out amplitudeLimitLower, out amplitudeLimitUpper);
                        tmpProg.Add(programConfig);
                        //write the amplitude limits for the program (using future authenticated commands)
                        initReturnInfo.Add(m_summit.zAuthStimWriteAmplitudeLimits(manualGroupNums[iGroup].Value, 0, (byte)amplitudeLimitLower, (byte)amplitudeLimitUpper));

                        //write other program parameters to INS
                        initReturnInfo.AddRange(m_summit.zAuthStimWriteProgram(manualGroupNums[iGroup].Value, 0, programConfig));

                        // Go through init return
                        foreach (APIReturnInfo aReturn in initReturnInfo)
                        {
                            if (aReturn.RejectCode != 0)
                            {
                                Console.WriteLine("Error during Manual Stim configuration. Error descriptor:" + aReturn.Descriptor);
                            }
                        }

                    }
                }
                else
                {
                    //get group parameters
                    Console.WriteLine("Reading stimulation info from INS...");
                    List<GroupNumber> allGroupNums = new List<GroupNumber>{ GroupNumber.Group0, GroupNumber.Group1, GroupNumber.Group2, GroupNumber.Group3 };
                    List<ActiveGroup> allGroupActives = new List<ActiveGroup>{ ActiveGroup.Group0, ActiveGroup.Group1, ActiveGroup.Group2, ActiveGroup.Group3 };
                    TherapyGroup groupInfo;
                    for (int iGroup = 0; iGroup < allGroupNums.Count; iGroup++)
                    {
                        m_summit.ReadStimGroup(allGroupNums[iGroup], out groupInfo);
                        List<TherapyProgram> programs = groupInfo.Programs;

                        if (groupInfo.Valid)
                        {
                            manualGroupNums.Add(allGroupNums[iGroup]);
                            manualGroupActives.Add(allGroupActives[iGroup]);

                            Console.WriteLine(String.Format("Group {0}:",iGroup));
                            List<int> groupProgs = new List<int>();
                            int iList = 0;
                            foreach (TherapyProgram iProgram in programs)
                            {
                                if (iProgram.Valid)
                                {
                                    groupProgs.Add(iList);
                                    Console.WriteLine("\t"+String.Format("Program {0}",iList));
                                }
                                iList++;
                            }
                            validPrograms.Add(groupProgs);
                        }

                        if (iGroup == 0)
                        {
                            //get current stim amp in program 0
                            defaultStimAmp = programs[0].AmplitudeInMilliamps;
                        }
                    }

                }

                startManualStim = parameters.GetParam("ManualStim.StartButton", typeof(string));
                stopManualStim = parameters.GetParam("ManualStim.StopButton", typeof(string));
                upAmp = parameters.GetParam("ManualStim.IncrementAmpButton", typeof(string));
                downAmp = parameters.GetParam("ManualStim.DecrementAmpButton", typeof(string));
                upFreq = parameters.GetParam("ManualStim.IncrementFreqButton", typeof(string));
                downFreq = parameters.GetParam("ManualStim.DecrementFreqButton", typeof(string));
                upPW= parameters.GetParam("ManualStim.IncrementPWButton", typeof(string));
                downPW = parameters.GetParam("ManualStim.DecrementPWButton", typeof(string));

                changeAmpAmount = parameters.GetParam("ManualStim.ChangeAmpAmountMilliAmps", typeof(double));
                changeFreqAmount = parameters.GetParam("ManualStim.ChangeFreqAmountHz", typeof(double));
                changePWAmount = parameters.GetParam("ManualStim.ChangePWAmountMicroSeconds", typeof(int));
            }


            ////Configure Stim Sweep===========================================================
            Console.WriteLine();
            bool doSinglePulseSweep = parameters.GetParam("StimSweep.SinglePulseSweep.Enabled", typeof(bool));
            bool doBurstSweep = parameters.GetParam("StimSweep.StimBurstSweep.Enabled", typeof(bool));
            GroupNumber? stimSweepGroupNum = null;
            ActiveGroup? stimSweepGroupActive = null;
            string startStimSweep = null;
            int pulseBurstPauseDuration=0;

            TherapyGroup stimSweepGroupConfig = new TherapyGroup();
            SummitUtils.StimProgramParameters stimSweepProgramParams = new SummitUtils.StimProgramParameters();
            SummitUtils.SweepConfigParameters configParameters = new SummitUtils.SweepConfigParameters();
            //configure single pulse sweep and make the sweeper object
            if (doSinglePulseSweep)
            {
                Console.WriteLine("Configuring stim sweep (single pulse)...");

                SummitUtils.configureStimSweep(parameters, m_summit, "SinglePulseSweep", out m_singlePulseSweeper, 
                    ref stimSweepGroupConfig, ref stimSweepProgramParams, ref configParameters);

                stimSweepGroupNum = configParameters.stimSweepGroupNum;
                stimSweepGroupActive = configParameters.stimSweepGroupActive;
            }


            //configure stim burst sweep and make the sweeper object
            if (doBurstSweep)
            {
                Console.WriteLine("Configuring stim sweep (stim bursts)...");
                SummitUtils.configureStimSweep(parameters, m_summit, "StimBurstSweep", out m_stimBurstSweeper,
                    ref stimSweepGroupConfig, ref stimSweepProgramParams, ref configParameters);

                stimSweepGroupNum = configParameters.stimSweepGroupNum;
                stimSweepGroupActive = configParameters.stimSweepGroupActive;
            }

            //if either of them are enabled, then stim sweeping is enabled
            if(doSinglePulseSweep || doBurstSweep)
            {
                if (useAuthCommands)
                {
                    Console.WriteLine("Configuring stim sweep group and program...");
                    List<APIReturnInfo> initReturnInfo = new List<APIReturnInfo>();

                    //first configure group
                    stimSweepGroupConfig.PulseWidthLowerLimit = (byte)configParameters.pulseWidthLowerLimit;
                    stimSweepGroupConfig.PulseWidthUpperLimit = (byte)configParameters.pulseWidthUpperLimit;
                    stimSweepGroupConfig.RatePeriod = (ushort)configParameters.startingRatePeriod;
                    stimSweepGroupConfig.RatePeriodLowerLimit = (ushort)configParameters.ratePeriodLowerLimit;
                    stimSweepGroupConfig.RatePeriodUpperLimit = (ushort)configParameters.ratePeriodUpperLimit;

                    initReturnInfo.Add(m_summit.StimChangeActiveGroup(stimSweepGroupActive.Value));
                    initReturnInfo.Add(m_summit.StimChangeTherapyOff(false));
                    Thread.Sleep(1000);
                    initReturnInfo.Add(m_summit.zAuthStimModifyClearGroup(stimSweepGroupNum.Value));
                    initReturnInfo.AddRange(m_summit.zAuthStimWriteGroup(stimSweepGroupNum.Value, stimSweepGroupConfig));

                    //next configure program
                    stimSweepProgramParams.amplitude = configParameters.startingAmp;
                    stimSweepProgramParams.ampLower = configParameters.ampLowerLimit;
                    stimSweepProgramParams.ampUpper = configParameters.ampUpperLimit;
                    stimSweepProgramParams.pulseWidth = configParameters.startingPulseWidth;

                    int amplitudeLimitLowerBlah, amplitudeLimitUpperBlah; //just placeholders, don't need these outputs
                    TherapyProgram stimSweepProgram = new TherapyProgram();
                    SummitUtils.ConfigureStimProgram(parameters, "StimSweep", 0, out stimSweepProgram, out amplitudeLimitLowerBlah, out amplitudeLimitUpperBlah, stimSweepProgramParams);
                    initReturnInfo.Add(m_summit.zAuthStimWriteAmplitudeLimits(stimSweepGroupNum.Value, 0, (byte)configParameters.ampLowerLimit, (byte)configParameters.ampUpperLimit));
                    initReturnInfo.AddRange(m_summit.zAuthStimWriteProgram(stimSweepGroupNum.Value, 0, stimSweepProgram));

                    // Go through init return
                    foreach (APIReturnInfo aReturn in initReturnInfo)
                    {
                        if (aReturn.RejectCode != 0)
                        {
                            Console.WriteLine("Error during Stim Sweep configuration. Error descriptor:" + aReturn.Descriptor);
                        }
                    }
                } else
                {
                    Console.WriteLine("Not using Auth commands, make sure the group and program (0) is configured correctly on the RLP!");
                }

                //get start button
                startStimSweep = parameters.GetParam("StimSweep.StartButton", typeof(string));

                //get pause time between single pulse and burst sweep if both are active
                pulseBurstPauseDuration = parameters.GetParam("StimSweep.PulseToBurstPauseMilliseconds", typeof(int));
            }


            ////Configure Closed-Loop Stim=====================================================
            bool doClosedLoopStim = parameters.GetParam("ClosedLoopStim.Enabled", typeof(bool));
            string startClosedLoopStim = null, stopClosedLoopStim = null;
            GroupNumber? closedLoopGroupNum = null;
            ActiveGroup? closedLoopGroupActive = null;

            //TODO: Implement check that group numbers for manual, sweep, and closed-loop stim are not the same!
            //TODO: Implement check that the electrodes for manual, sweep, and closed-loop stim are not the same!
            //TODO: Implement check that no buttons are used for multiple things!

            if (doClosedLoopStim)
            {
                Console.WriteLine();
                Console.WriteLine("Configuring closed-loop stim...");
                List<APIReturnInfo> initReturnInfo = new List<APIReturnInfo>();

                //set up a group and write to INS
                TherapyGroup groupConfig;
                GroupNumber tmpClosedLoopGroupNum;
                ActiveGroup tmpClosedLoopGroupActive;

                SummitUtils.ConfigureStimGroup(parameters, "ClosedLoopStim", null, out groupConfig, out tmpClosedLoopGroupNum, out tmpClosedLoopGroupActive);

                initReturnInfo.Add(m_summit.StimChangeActiveGroup(closedLoopGroupActive.Value));
                initReturnInfo.Add(m_summit.StimChangeTherapyOff(false));
                Thread.Sleep(1000);
                initReturnInfo.Add(m_summit.zAuthStimModifyClearGroup(closedLoopGroupNum.Value));
                initReturnInfo.AddRange(m_summit.zAuthStimWriteGroup(closedLoopGroupNum.Value, groupConfig));

                closedLoopGroupNum = tmpClosedLoopGroupNum;
                closedLoopGroupActive = tmpClosedLoopGroupActive;

                //set up programs (one for each class)
                //first get number of programs
                int nPrograms = parameters.GetParam("ClosedLoopStim.nClasses", typeof(int));

                //make all the programs
                for (int iProg = 0; iProg < nPrograms; iProg++)
                {
                    //make program
                    TherapyProgram programConfig;
                    int amplitudeLimitLower, amplitudeLimitUpper;
                    SummitUtils.ConfigureStimProgram(parameters, "ClosedLoopStim", iProg, out programConfig, out amplitudeLimitLower, out amplitudeLimitUpper);

                    //write program to INS
                    initReturnInfo.AddRange(m_summit.zAuthStimWriteProgram(closedLoopGroupNum.Value, (byte)iProg, programConfig));

                    //also write the amplitude limits for the program (using future authenticated commands)
                    initReturnInfo.Add(m_summit.zAuthStimWriteAmplitudeLimits(closedLoopGroupNum.Value, (byte)iProg, (byte)amplitudeLimitLower, (byte)amplitudeLimitUpper));

                    // Go through init return
                    foreach (APIReturnInfo aReturn in initReturnInfo)
                    {
                        if (aReturn.RejectCode != 0)
                        {
                            Console.WriteLine("Error during closed-loop stim configuration, may not function properly. Error descriptor:" + aReturn.Descriptor);
                        }
                    }
                }

                //start and stop buttons
                startClosedLoopStim = parameters.GetParam("ClosedLoopStim.StartButton", typeof(string));
                stopClosedLoopStim = parameters.GetParam("ClosedLoopStim.StopButton", typeof(string));
            }


            ////Initialize closed-loop threads===============================================

            // setup thread-safe shared resources
            ThreadResources sharedResources = new ThreadResources();
            sharedResources.TDbuffer = m_TDBuffer;
            sharedResources.savingBuffer = m_dataSavingBuffer;
            sharedResources.summit = m_summit;
            sharedResources.saveDataFileName = m_dataFileName;
            sharedResources.samplingRate = doSensing ? samplingRate : TdSampleRates.Disabled;
            sharedResources.timingLogFile = m_timingLogFile;
            sharedResources.parameters = parameters;

            bool streamToOpenEphys = parameters.GetParam("StreamToOpenEphys", typeof(bool));

            // have to have sensing set up to do streaming to Open-Ephys! Throw error if no sensing
            if (streamToOpenEphys && !doSensing)
            {
                throw new Exception("Need to enable sensing if you want to stream to Open Ephys!");
            }

            // streaming to Open Ephys, set up connection
            StreamingThread sendSenseThread = null;
            StreamingThread getStimThread = null;

            if (streamToOpenEphys)
            {
                Console.WriteLine();
                //first, perform hand-shake

                //Message structure on handshake:
                //
                //int number of channels
                //int buffer size
                //
                Console.WriteLine("Attempting to connect to Open-Ephys...");

                int zmqPort = parameters.GetParam("Sense.ZMQPort", typeof(int));
                using (ResponseSocket senseSocket = new ResponseSocket())
                {
                    senseSocket.Bind("tcp://*:" + zmqPort);
                    string inMessage;
                    byte[] outMessage;

                    inMessage = senseSocket.ReceiveFrameString();

                    if (inMessage == "InitTD")
                    {
                        outMessage = BitConverter.GetBytes(m_TDBuffer.getNumChans());
                        outMessage = m_TDBuffer.Concatenate(outMessage, BitConverter.GetBytes(m_TDBuffer.getBufferSize()));
                        senseSocket.SendFrame(outMessage);
                        Console.WriteLine("Connection with Open-Ephys Established");
                    }
                    else
                    {
                        Console.WriteLine("Handshake with Open-Ephys failed!");
                        Console.WriteLine("Press any key to quit");
                        Console.ReadKey();
                        return;
                    }
                }

                // Make the Open-Ephys streaming threads
                sendSenseThread = new StreamingThread(ThreadType.sense);
                //getStimThread = new StreamingThread(ThreadType.stim);

                // Start the threads to stream to summit source and summit sink in open ephys
                Console.WriteLine("Press any key to start streaming to Open-Ephys");
                Console.ReadKey();
                Console.WriteLine("Streaming Started");
                sendSenseThread.StartThread(ref sharedResources);
                //getStimThread.StartThread(ref sharedResources);

            }

            //start data saving thread
            StreamingThread dataSaveThread = new StreamingThread(ThreadType.dataSave);
            dataSaveThread.StartThread(ref sharedResources);

            //finally register the listeners to start getting data from the INS
            if (doSensing)
            {
                m_summit.DataReceivedTDHandler += SummitTimeDomainPacketReceived;
                //TODO: Add frequency domain streams
                //theSummit.dataReceivedPower += theSummit_DataReceived_Power;
                //theSummit.dataReceivedFFT += theSummit_DataReceived_FFT;
                //theSummit.dataReceivedAccel += theSummit_DataReceived_Accel;
            }
            m_summit.UnexpectedLinkStatusHandler += SummitLinkStatusReceived;


            ////Start stim control (user control)=============================================
            Console.WriteLine();
            Console.WriteLine("Finished setup, giving stim control over to user. Press any key to continue.");
            Console.WriteLine();
            ConsoleKeyInfo thekey = Console.ReadKey();

            string quitKey = parameters.GetParam("QuitButton", typeof(string));
            string stimStatusKey = parameters.GetParam("StimStatusButton", typeof(string));

            int manualStimGroup = 0, manualStimProg = 0;
            ActiveGroup? defaultGroupActive = null;
            GroupNumber? defaultGroupNum = null;
            bool doStim = true;
            if (doManualStim)
            {
                //default be at manual stim
                defaultGroupActive = manualGroupActives[0]; 
                defaultGroupNum = manualGroupNums[0];
            } else if (doBurstSweep || doSinglePulseSweep)
            {
                //otherwise default to stim sweep
                defaultGroupActive = stimSweepGroupActive;
                defaultGroupNum = stimSweepGroupNum;
            } else if (doClosedLoopStim)
            {
                //only thing left is closed-loop stim
                defaultGroupActive = closedLoopGroupActive;
                defaultGroupNum = closedLoopGroupNum;
            } else 
            {
                //no stim was set up
                doStim = false;
            }
            if (doStim)
            {
                m_summit.StimChangeActiveGroup(defaultGroupActive.Value);
            }

            double? currentStimAmp = defaultStimAmp, newStimAmp; //keep track of stim amp
            double? currentStimFreq, newStimFreq; //and stim freq
            int? currentStimPW, newStimPW; //and stim pulse wdith
            bool stimCurrentlyOn = false; //indicates if stim is on

            APIReturnInfo bufferInfo = new APIReturnInfo();
            while (thekey.KeyChar.ToString() != quitKey)
            {
                //interragate stim status
                if(thekey.KeyChar.ToString() == stimStatusKey && doStim)
                {
                    int? pw;
                    double? amp, freq;
                    if (SummitUtils.CheckCurrentStimParameters(m_summit, manualGroupNums[manualStimGroup].Value, manualStimProg, out amp, out pw, out freq))
                    {
                        Console.WriteLine("Group " + manualGroupNums[manualStimGroup].Value.ToString() + ", Program " + manualStimProg.ToString() + ": Amp = " + amp.Value
                            + "mA, PW = " + pw.Value + "us, Frequency = " + freq.Value + "Hz");
                    }
                }

                //manual stim controls check
                if (doManualStim)
                {

                    //first check for group switching
                    for (int iManGroup = 0; iManGroup < manualGroupButtons.Count; iManGroup++)
                    {
                        if (thekey.KeyChar.ToString() == manualGroupButtons[iManGroup])
                        {
                            if (iManGroup==manualStimGroup)
                            {
                                Console.WriteLine(String.Format("Already in Manual stim group {0}!", iManGroup));
                                continue;
                            }

                            //reduce amplitude to 0 and turn stim off
                            if (stimCurrentlyOn)
                            {
                                bufferInfo = m_summit.StimChangeStepAmp((byte)manualStimProg, -1 * currentStimAmp.Value, out newStimAmp);
                                if (newStimAmp != null)
                                {
                                    currentStimAmp = newStimAmp;
                                }
                                bufferInfo = m_summit.StimChangeTherapyOff(false);
                                Thread.Sleep(1000);
                            }

                            //change group
                            bufferInfo = m_summit.StimChangeActiveGroup(manualGroupActives[iManGroup].Value);
                            manualStimGroup = iManGroup;
                            Console.WriteLine(String.Format("Switching Manual Stim to group {0}", iManGroup));

                            //turn therapy back on if it was on before
                            if (stimCurrentlyOn)
                            {
                                bufferInfo = m_summit.StimChangeTherapyOn();
                            }
                        }
                    }

                    //next check for program switching
                    for (int iManProg = 0; iManProg < manualProgramButtons.Count; iManProg++)
                    {
                        if (thekey.KeyChar.ToString() == manualProgramButtons[iManProg])
                        {
                            if (iManProg == manualStimProg)
                            {
                                Console.WriteLine(String.Format("Already in Manual stim program {0}!", iManProg));
                                continue;
                            }

                            //reset amplitude of current program to 0 if stimulation is currently on
                            if (stimCurrentlyOn)
                            {
                                bufferInfo = m_summit.StimChangeStepAmp((byte)manualStimProg, -1 * currentStimAmp.Value, out newStimAmp);
                                if (newStimAmp != null)
                                {
                                    currentStimAmp = newStimAmp;
                                }
                                if (currentStimAmp != 0)
                                {
                                    Console.WriteLine("Warning: Stimulation wasn't turned off when switching programs!");
                                }
                            }

                            //switch program index
                            manualStimProg = validPrograms[manualStimGroup][iManProg];
                            Console.WriteLine(String.Format("Switching Manual Stim to Program {0}", iManProg));
                        }
                    }

                    //next check for start/stop buttons
                    if (thekey.KeyChar.ToString() == startManualStim)
                    {
                        bufferInfo = m_summit.StimChangeTherapyOn();
                        Thread.Sleep(1000);
                        // Reset POR if set
                        if (bufferInfo.RejectCodeType == typeof(MasterRejectCode)
                            && (MasterRejectCode)bufferInfo.RejectCode == MasterRejectCode.ChangeTherapyPor)
                        {
                            // Inform user
                            Console.WriteLine("POR set, resetting...");
                            // Reset POR
                            bufferInfo = SummitUtils.resetPOR(m_summit);
                        }
                        Console.WriteLine("Starting Manual Stim");
                        stimCurrentlyOn = true;
                    }
                    else if (thekey.KeyChar.ToString() == stopManualStim & stimCurrentlyOn)
                    {
                        m_summit.StimChangeStepAmp((byte)manualStimProg, -1 * currentStimAmp.Value, out newStimAmp);
                        if (newStimAmp!=null)
                        {
                            currentStimAmp = newStimAmp;
                        }
                        m_summit.StimChangeTherapyOff(false);
                        Thread.Sleep(1000);
                        Console.WriteLine("Stopping Manual Stim");
                        stimCurrentlyOn = false;
                    }

                    //then check for stim parameter buttons
                    if (thekey.KeyChar.ToString() == downAmp)
                    {
                        // Decrease the Amplitude
                        bufferInfo = m_summit.StimChangeStepAmp((byte)manualStimProg, -1 * changeAmpAmount.Value, out newStimAmp);
                        if (newStimAmp != null)
                        {
                            currentStimAmp = newStimAmp;
                        }
                    }
                    else if (thekey.KeyChar.ToString() == upAmp)
                    {
                        // Increment the Amplitude
                        bufferInfo = m_summit.StimChangeStepAmp((byte)manualStimProg, changeAmpAmount.Value, out newStimAmp);
                        if (newStimAmp != null)
                        {
                            currentStimAmp = newStimAmp;
                        }
                    }
                    else if (thekey.KeyChar.ToString() == downFreq)
                    {
                        // Decrease the Stimulation Frequency, keep to sense friendly values (
                        bufferInfo = m_summit.StimChangeStepFrequency(-1*changeFreqAmount.Value, true, out newStimFreq);
                        if (newStimFreq != null)
                        {
                            currentStimFreq = newStimFreq;
                        }
                    }
                    else if (thekey.KeyChar.ToString() == upFreq)
                    {
                        // Increment the Stimulation Frequency, keep to sense friendly values
                        bufferInfo = m_summit.StimChangeStepFrequency(changeFreqAmount.Value, true, out newStimFreq);
                        if (newStimFreq != null)
                        {
                            currentStimFreq = newStimFreq;
                        }
                    }
                    else if (thekey.KeyChar.ToString() == upPW)
                    {
                        //Decrease pulse width
                        bufferInfo = m_summit.StimChangeStepPW((byte)manualStimProg, -1 * changePWAmount.Value, out newStimPW);
                        if (newStimPW != null)
                        {
                            currentStimPW = newStimPW;
                        }
                    }
                    else if (thekey.KeyChar.ToString() == downPW)
                    {
                        //Increment pulse width
                        bufferInfo = m_summit.StimChangeStepPW((byte)manualStimProg, changePWAmount.Value, out newStimPW);
                        if (newStimPW != null)
                        {
                            currentStimPW = newStimPW;
                        }
                    }
                }

                if (doSinglePulseSweep || doBurstSweep)
                {
                    //button for starting the stim sweep
                    bool sweepAborted = false;
                    if (thekey.KeyChar.ToString() == startStimSweep)
                    {
                        if (doSinglePulseSweep)
                        {
                            Console.WriteLine("Starting stimulation sweep (single pulse):");
                            m_summit.StimChangeActiveGroup(stimSweepGroupActive.Value);
                            m_singlePulseSweeping = true;
                            sweepAborted = m_singlePulseSweeper.Sweep();
                            m_singlePulseSweeping = false;

                            if (!sweepAborted)
                            {
                                Console.WriteLine("Finished stimulation sweep (single pulse)");
                            }
                            Console.WriteLine();
                            Thread.Sleep(pulseBurstPauseDuration);
                        }

                        if (doBurstSweep && !sweepAborted)
                        {
                            Console.WriteLine("Starting stimulation sweep (bursts):");
                            m_stimBurstSweeping = true;
                            sweepAborted = m_stimBurstSweeper.Sweep();
                            m_stimBurstSweeping = false;
                            Console.WriteLine("Finished stimulation sweep (bursts):");
                            if (!sweepAborted)
                            {
                                Console.WriteLine("Finished stimulation sweep (single pulse)");
                            }
                            Console.WriteLine();
                        }

                        if (doManualStim)
                        {
                            //switch back to manual control group
                            Console.WriteLine("Finished stimulation sweep, switching back to manual control group");
                            m_summit.StimChangeActiveGroup(manualGroupActives[manualStimGroup].Value);
                        }
                    }
                }

                //do closed-loop control check
                if (doClosedLoopStim)
                {
                    if (thekey.KeyChar.ToString() == startClosedLoopStim) //start stim
                    {
                        m_summit.StimChangeActiveGroup(closedLoopGroupActive.Value);

                        DateTime onset = DateTime.Now;
                        m_summit.StimChangeTherapyOn();
                        Thread.Sleep(1000);
                        // Reset POR if set
                        if (bufferInfo.RejectCodeType == typeof(MasterRejectCode)
                            && (MasterRejectCode)bufferInfo.RejectCode == MasterRejectCode.ChangeTherapyPor)
                        {
                            Console.WriteLine("POR set, resetting...");
                            bufferInfo = SummitUtils.resetPOR(m_summit);
                        }

                        //log event
                        m_timingLogFile.WriteLine("3 " + DateTime.Now.Ticks.ToString());
                        m_summit.LogCustomEvent(onset, DateTime.Now, "ClosedLoopStimStart", "ClosedLoopStimStart");

                        m_summit.StimChangeActiveGroup(manualGroupActives[manualStimGroup].Value);

                        Console.WriteLine("Starting Closed Loop Stim");

                    }
                    else if (thekey.KeyChar.ToString() == stopClosedLoopStim) // stop stim
                    {
                        DateTime onset = DateTime.Now;

                        m_summit.StimChangeActiveGroup(closedLoopGroupActive.Value);
                        m_summit.StimChangeTherapyOff(false);
                        Thread.Sleep(1000);
                        m_summit.StimChangeActiveGroup(manualGroupActives[manualStimGroup].Value);

                        //log event
                        m_timingLogFile.WriteLine("3 " + DateTime.Now.Ticks.ToString());
                        m_summit.LogCustomEvent(onset, DateTime.Now, "ClosedLoopStimStart", "ClosedLoopStimStart");

                        Console.WriteLine("Stopping Closed Loop Stim");

                    }
                }

                // Print out the command's status
                Console.WriteLine(" Command Status:" + bufferInfo.Descriptor);
                bufferInfo = new APIReturnInfo();

                thekey = Console.ReadKey(true);

            }

            ////Close and cleaning up======================================================
            // stop threads
            if (streamToOpenEphys)
            {
                sendSenseThread.StopThread();
                //getStimThread.StopThread();
            }
            dataSaveThread.StopThread();

            // Stop Stim
            if (doManualStim)
            {
                m_summit.StimChangeActiveGroup(manualGroupActives[manualStimGroup].Value);
                m_summit.StimChangeTherapyOff(false);
                Thread.Sleep(1000);
            }
            if (doClosedLoopStim)
            {
                m_summit.StimChangeActiveGroup(closedLoopGroupActive.Value);
                m_summit.StimChangeTherapyOff(false);
                Thread.Sleep(1000);
            }

            // Object Disposal
            Console.WriteLine("");
            Console.WriteLine("Disposing Summit");
            theSummitManager.Dispose();
            m_debugFile.closeFile();
            m_timingLogFile.closeFile();

            // Prompt user for final keypress before closing down the program.
            Console.WriteLine("Press key to exit");
            Console.ReadKey();

        }



        private static void SummitLinkStatusReceived(object sender, UnexpectedLinkStatusEvent statusEvent)
        {
            if(statusEvent.TheLinkStatus.OOR)
            {
                //notify user
                Console.WriteLine("Stimulation engine is out of range!");
                if (m_singlePulseSweeping)
                {
                    Console.WriteLine("Aborting stimulation sweep...");
                    m_singlePulseSweeper.m_stimOutOfRangeFlag = true;
                }
                else if (m_stimBurstSweeping)
                {
                    Console.WriteLine("Aborting stimulation sweep...");
                    m_stimBurstSweeper.m_stimOutOfRangeFlag = true;
                }
                m_summit.ResetErrorFlags(StatusBits.StatusChanged2);
                m_summit.ResetErrorFlags(StatusBits.CrcError);
                m_summit.ResetErrorFlags(StatusBits.Por);
            }
        }


        
        // Sensing data received event handlers
        private static void SummitTimeDomainPacketReceived(object sender, SensingEventTD TdSenseEvent)
        {
            m_TDPacketCount++;

            //log time received to timing file
            string timestamp = DateTime.Now.Ticks.ToString();
            string packetNum = TdSenseEvent.Header.DataTypeSequence.ToString();
            int nSamples = TdSenseEvent.ChannelSamples[0].Count;
            double i = TdSenseEvent.ChannelSamples[0][0];
            m_timingLogFile.WriteLine("0 " + timestamp + " " + packetNum + " " + nSamples);

            //get the minimum number of samples in all the channels, but show warning if there are not same number of samples for all channels
            foreach (SenseTimeDomainChannel chan in TdSenseEvent.ChannelSamples.Keys)
            {
                if (TdSenseEvent.ChannelSamples[chan].Count < nSamples)
                {
                    nSamples = TdSenseEvent.ChannelSamples[chan].Count;
                    Console.WriteLine("!Warning: Not all channles have same number of samples!");
                }
            }

            //log to debug file
            m_debugFile.WriteLine(m_TDPacketCount + " " + TdSenseEvent.Header.DataTypeSequence + " " + TdSenseEvent.GenerationTimeEstimate.Ticks + " " + DateTime.Now.Ticks.ToString() + " " + TdSenseEvent.Header.SystemTick.ToString() + " " + nSamples);

            // Annouce to console that a packet was received by handler
            if (notifyCTM)
            {
                Console.WriteLine("TD Packet Received, Global SeqNum:" + TdSenseEvent.Header.GlobalSequence.ToString()
                    + "; Time Generated:" + TdSenseEvent.Header.SystemTick.ToString() + "; Time Event Called:" + DateTime.Now.Ticks.ToString());
            }

            // check if we dropped a packet
            int nDroppedPackets;
            if (!SummitUtils.CheckDroppedPackets(TdSenseEvent.Header.DataTypeSequence, m_prevPacketNum, out nDroppedPackets))
            {
                return;
            }

            //update packet numbers, times, samples
            SummitUtils.InterpolationParameters interpParams;

            interpParams.prevPacketNum = m_prevPacketNum;
            m_prevPacketNum = TdSenseEvent.Header.DataTypeSequence;

            //long packetTime = TdSenseEvent.GenerationTimeEstimate.Ticks; //ticks in 100 ns
            ushort packetTime = TdSenseEvent.Header.SystemTick; //ticks in 100 us
            interpParams.prevPacketTime = m_prevPacketTime;
            m_prevPacketTime = packetTime;

            interpParams.prevPacketNSamples = m_prevPacketNSamples;
            m_prevPacketNSamples = nSamples;

            //estimate number of times SystemTick looped (since it's an uint16, 65535 max value, so can loop if more than 6.5 s)
            //get estimate timestamp of current packet
            long packetEstTime = TdSenseEvent.GenerationTimeEstimate.Ticks; //ticks in 100 ns
            //save difference between current and previous packet
            interpParams.prevPacketEstTimeDiff = packetEstTime - m_prevPacketEstTime;
            //one loop is 65536 values
            int nloops = (int)Math.Floor((double)(packetEstTime - m_prevPacketEstTime)/65536000);
            m_prevPacketEstTime = packetEstTime;

            //number of loops should never be negative
            if (nloops < 0)
            {
                Console.WriteLine("Previous packet INS time estimate is greater than current!");
                nloops = 0;
            }

            //now do the interpolation
            if (!m_firstPacket && nDroppedPackets != 0 && interp)
            {
                interpParams.nChans = m_nTDChans;
                interpParams.timestampDiff = packetTime - interpParams.prevPacketTime;
                interpParams.secondsToTimeStamp = 10000; //since using SystemTick which is in 100us
                interpParams.nDroppedPackets = nDroppedPackets;
                interpParams.samplingRate = m_samplingRate;
                interpParams.prevValues = m_prevLastValues;
                
                SummitUtils.InterpolateDroppedSamples(m_TDBuffer, m_dataSavingBuffer, TdSenseEvent, interpParams);
            }

            m_firstPacket = false;

            //get data from packet and add to buffers
            double[,] chanData = new double[m_nTDChans, nSamples];

            for (int iSample = 0; iSample < nSamples; iSample++)
            {

                int iChan = 0;
                foreach (SenseTimeDomainChannel chan in TdSenseEvent.ChannelSamples.Keys)
                {
                    double sampleVolts = TdSenseEvent.ChannelSamples[chan][iSample];

                    chanData[iChan, iSample] = sampleVolts;

                    //save last value for interpolation of missing packets
                    if (iSample + 1 == nSamples)
                    {
                        m_prevLastValues[iChan] = chanData[iChan, iSample];
                    }
                    iChan++;
                }

            }
            //add data to buffers
            //m_TDBuffer.addData(chanData, TdSenseEvent.Header.DataTypeSequence, (double)TdSenseEvent.GenerationTimeEstimate.Ticks, 0);
            //m_dataSavingBuffer.addData(chanData, TdSenseEvent.Header.DataTypeSequence, (double)TdSenseEvent.GenerationTimeEstimate.Ticks, 0);
            m_TDBuffer.addData(chanData, TdSenseEvent.Header.DataTypeSequence, (double)TdSenseEvent.Header.SystemTick, 0);
            m_dataSavingBuffer.addData(chanData, TdSenseEvent.Header.DataTypeSequence, (double)TdSenseEvent.Header.SystemTick, 0);

            // Log some inforamtion about the received packet out to file
            m_summit.LogCustomEvent(TdSenseEvent.GenerationTimeEstimate, DateTime.Now, "TdPacketReceived", TdSenseEvent.Header.GlobalSequence.ToString());
        }


        private static void theSummit_DataReceived_FFT(object sender, SensingEventFFT e)
        {
            // Log the received packet out to file
            //theSummit.LogCustomJSON("FFT Received", e.theFftPacket.Header.GlobalSequence.ToString());

            // Annouce to console that packet was received by handler
            Console.WriteLine("FFT Packet Received, Global SeqNum:" + e.Header.GlobalSequence.ToString()
                + "; Time Generated:" + e.GenerationTimeEstimate.Ticks.ToString() + "; Time Event Called:" + DateTime.Now.Ticks.ToString());
        }


        private static void theSummit_DataReceived_Power(object sender, SensingEventPower e)
        {
            // Log the received packet out to file
            //theSummit.LogCustomJSON("Power Received", e.thePowerPacket.Header.GlobalSequence.ToString());

            // Annouce to console that packet was received by handler
            Console.WriteLine("Power Packet Received, Global SeqNum:" + e.Header.GlobalSequence.ToString()
                + "; Time Generated:" + e.GenerationTimeEstimate.Ticks.ToString() + "; Time Event Called:" + DateTime.Now.Ticks.ToString());
        }


        private static void theSummit_DataReceived_Accel(object sender, SensingEventAccel e)
        {
            // Log the received packet out to file
            //theSummit.LogCustomJSON("Accel Received", e.theAccelPacket.Header.GlobalSequence.ToString());

            // Annouce to console that packet was received by handler
            Console.WriteLine("AccelPacket Received, Global SeqNum:" + e.Header.GlobalSequence.ToString()
                + "; Time Generated:" + e.GenerationTimeEstimate.Ticks.ToString() + "; Time Event Called:" + DateTime.Now.Ticks.ToString());
        }


    }
}
