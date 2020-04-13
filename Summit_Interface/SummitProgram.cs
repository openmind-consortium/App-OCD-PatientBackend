using Medtronic.NeuroStim.Olympus.Commands;
using Medtronic.NeuroStim.Olympus.DataTypes.Core;
using Medtronic.NeuroStim.Olympus.DataTypes.DeviceManagement;
using Medtronic.NeuroStim.Olympus.DataTypes.Measurement;
using Medtronic.NeuroStim.Olympus.DataTypes.PowerManagement;
using Medtronic.NeuroStim.Olympus.DataTypes.Sensing;
using Medtronic.NeuroStim.Olympus.DataTypes.Therapy;
using Medtronic.SummitAPI.Classes;
using Medtronic.SummitAPI.Events;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;

namespace Summit_Interface
{
    class SummitProgram
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;

        //Session experimental parameters
        static INSParameters parameters;

        //Buffers for storing data from the INS
        static INSBuffer m_TDBuffer; //time domain voltages
        static INSBuffer m_FFTBuffer; //Frequency domain
        static INSBuffer m_BPBuffer; //Band power
        static INSBuffer m_dataSavingBuffer; //saving to file buffer

        //Summit API object
        static SummitSystem m_summit;
        static SummitSystemWrapper m_summitWrapper;

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

        static endProgramWrapper m_exitProgram;

        //Main program
        [STAThread]
        static void Main(string[] args)
        {
            ////Initiation===============================================================

            //First, load in configuration parameters from JSON file
            string parametersFileName;

            if (File.Exists("C:/JSONFiles/SIPConfig.json"))
            {
                //load in frome default file if it exists
                parametersFileName = "C:/JSONFiles/SIPConfig.json";
            }
            else
            {
                //otherwise, ask user to give a file
                OpenFileDialog parametersFileDialog = new OpenFileDialog();
                parametersFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                parametersFileDialog.FilterIndex = 0;
                parametersFileDialog.RestoreDirectory = true;
                parametersFileDialog.Multiselect = false;
                parametersFileDialog.Title = "Select Summit parameters JSON file";
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
            }

            parameters = new INSParameters(parametersFileName);

            //hide console window if desired
            bool hideConsole = parameters.GetParam("HideConsole", typeof(bool));
            const int SW_HIDE = 0;
            const int SW_SHOW = 5;
            var handle = GetConsoleWindow();

            if (hideConsole)
            {
                ShowWindow(handle, SW_HIDE);
            }

            ConsoleKeyInfo input = new ConsoleKeyInfo();

            // set up buffers
            int numSenseChans = parameters.GetParam("Sense.nChans", typeof(int));
            int bufferSize = parameters.GetParam("Sense.BufferSize", typeof(int)); // Make sure this is not larger than the AudioSampleBuffer buffer that Open-Ephys uses! (1024 last time I checked)
            m_TDBuffer = new INSBuffer(numSenseChans, bufferSize);
            m_FFTBuffer = new INSBuffer(1, bufferSize);
            m_BPBuffer = new INSBuffer(numSenseChans * 2, bufferSize);
            m_dataSavingBuffer = new INSBuffer(numSenseChans, bufferSize);
            m_summitWrapper = new SummitSystemWrapper();

            //set up files
            m_dataFileName = parameters.GetParam("Sense.SaveFileName", typeof(string));
            if (m_dataFileName.Length > 4)
            {
                if (m_dataFileName.Substring(m_dataFileName.Length - 4, 4) == ".txt")
                {
                    m_dataFileName = m_dataFileName.Substring(0, m_dataFileName.Length - 4);
                }
            }

            //check if the file exists, and warn user that it will be overwritten if it does
            List<string> dataFilesNames = new List<string> { m_dataFileName + "-Timing.txt",
                m_dataFileName + "-Debug.txt", m_dataFileName + "-Impedance.txt" };

            bool filesExists = false;
            foreach (string filename in dataFilesNames)
            {
                if (File.Exists(filename))
                {
                    Console.WriteLine("Warning: " + filename + " already exsits, it will be overwritten!");
                    filesExists = true;
                }
            }


            m_timingLogFile = new ThreadsafeFileStream(m_dataFileName + "-Timing.txt");
            m_debugFile = new ThreadsafeFileStream(m_dataFileName + "-Debug.txt");
            System.IO.StreamWriter m_impedanceFile = new System.IO.StreamWriter(m_dataFileName + "-Impedance.txt");

            // Create a manager
            SummitManager theSummitManager = new SummitManager("OCD-Sense-Session");

            // setup thread-safe shared resources
            bool doSensing = parameters.GetParam("Sense.Enabled", typeof(bool));
            TdSampleRates samplingRate = TdSampleRates.Disabled; //default disabled, if we are sensing, we'll set the sampling rate below
            bool noDeviceTesting = parameters.GetParam("NoDeviceTesting", typeof(bool));
            m_exitProgram = new endProgramWrapper();

            ThreadResources sharedResources = new ThreadResources();
            sharedResources.TDbuffer = m_TDBuffer;
            sharedResources.savingBuffer = m_dataSavingBuffer;
            sharedResources.summitWrapper = m_summitWrapper;
            sharedResources.summitManager = theSummitManager;
            sharedResources.saveDataFileName = m_dataFileName;
            sharedResources.samplingRate = doSensing ? samplingRate : TdSampleRates.Disabled;
            sharedResources.timingLogFile = m_timingLogFile;
            sharedResources.parameters = parameters;
            sharedResources.testMyRCPS = noDeviceTesting;
            sharedResources.endProgram = m_exitProgram;
            sharedResources.enableTimeSync = parameters.GetParam("Sense.APITimeSync", typeof(bool));

            //now, establish connection to MyRC+S program
            StreamingThread myRCSThread = new StreamingThread(ThreadType.myRCpS);
            myRCSThread.StartThread(ref sharedResources);

            ////Connect to Device=========================================================++
            // Initialize the Summit Interface
            Console.WriteLine();
            Console.WriteLine("Creating Summit Interface...");
            Thread.Sleep(5000);
            ushort mode = (ushort)parameters.GetParam("TelemetryMode", typeof(int));

            // Connect to CTM and INS
            while (!SummitUtils.SummitConnect(theSummitManager, ref m_summit, ref m_summitWrapper, mode))
            {
                // Failed to connect, keep retrying
                //Console.WriteLine("Unable to establish connection, press 'r' to retry, or anything else to exit");
                //ConsoleKeyInfo retryKey = Console.ReadKey();

                //if (retryKey.KeyChar.ToString() != "r")

                Console.WriteLine("Unable to establish connection, retrying...");
                if (m_exitProgram.end)
                {
                    theSummitManager.Dispose();
                    return;
                }
            }

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

            /*
            System.IO.StreamWriter testingFile = new System.IO.StreamWriter("BatteryTesting.txt");
            string message;
            while (true)
            {
                commandInfo = m_summit.StimChangeTherapyOff(false);
                message = "Stim off \t " + commandInfo.RejectCodeType.ToString() +
                    '\t' + commandInfo.RejectCode.ToString() + '\t' + String.Format("{0:F}", DateTime.Now);
                testingFile.WriteLine(message);
                Console.WriteLine(message);
                if (commandInfo.RejectCode != 0)
                {
                    break;
                }

                Thread.Sleep(1000);

                commandInfo = m_summit.StimChangeTherapyOn();
                message = "Stim on \t " + commandInfo.RejectCodeType.ToString() +
                    '\t' + commandInfo.RejectCode.ToString() + '\t' + String.Format("{0:F}", DateTime.Now);
                testingFile.WriteLine(message);
                Console.WriteLine(message);
                if (commandInfo.RejectCode != 0)
                {
                    break;
                }

                Thread.Sleep(1000);
            }
            testingFile.Close();
            */

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
                string impedHeader = "";
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
                        Console.Write(String.Format("Chan {0}", iElec));
                        m_impedanceFile.Write(String.Format("Chan {0}", iElec));
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


            //if time-synching is enabled, do latency test
            if (parameters.GetParam("Sense.APITimeSync", typeof(bool)))
            {
                TimeSpan? span = null;
                m_summit.CalculateLatency(10, out span);
                if (span != null)
                {
                    Console.Write("Latency test: " + span?.ToString("c"));
                }
                else
                {
                    Console.WriteLine("Unable to perform latency test!");
                }
            }


            ////Configure Sensing============================================================

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
            bool enableTimeSync = parameters.GetParam("Sense.APITimeSync", typeof(bool));


            //initialize streaming threads variables
            m_nTDChans = parameters.GetParam("Sense.nChans", typeof(int));
            m_prevLastValues = new double[m_nTDChans];

            notifyCTM = parameters.GetParam("NotifyCTMPacketsReceived", typeof(bool));
            interp = parameters.GetParam("Sense.InterpolateMissingPackets", typeof(bool));

            //turn on actual sensing
            if (doSensing)
            {
                returnInfoBuffer = m_summit.WriteSensingEnableStreams(true, FFTEnabled, powerEnabled, false, false, true, enableTimeSync, false);

            }

            ////Initialize closed-loop threads===============================================

            bool streamToOpenEphys = parameters.GetParam("StreamToOpenEphys", typeof(bool));

            // have to have sensing set up to do streaming to Open-Ephys! Throw error if no sensing
            if (streamToOpenEphys && !doSensing)
            {
                throw new Exception("Need to enable sensing if you want to stream to Open Ephys!");
            }

            // streaming to Open Ephys, set up connection
            StreamingThread sendSenseThread = null;

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

                // Start the threads to stream to summit source and summit sink in open ephys
                Console.WriteLine("Press any key to start streaming to Open-Ephys");
                Console.ReadKey();
                Console.WriteLine("Streaming Started");
                sendSenseThread.StartThread(ref sharedResources);

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

            ////Start main loop==================================================
            Console.WriteLine();
            Console.WriteLine("Finished setup");
            Console.WriteLine();
            ConsoleKeyInfo thekey = new ConsoleKeyInfo();

            string quitKey = parameters.GetParam("QuitButton", typeof(string));

            bool keepCheckingKeys = true;

            while (thekey.KeyChar.ToString() != quitKey)
            {

                //check if child threads want main thread to close
                while (Console.KeyAvailable == false)
                {
                    if (m_exitProgram.end)
                    {
                        keepCheckingKeys = false;
                        break;
                    }
                    Thread.Sleep(250);
                }

                if (!keepCheckingKeys)
                {
                    break;
                }

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
            myRCSThread.StopThread();

            // Object Disposal
            Console.WriteLine("");
            Console.WriteLine("Disposing Summit");
            theSummitManager.Dispose();
            m_debugFile.closeFile();
            m_timingLogFile.closeFile();

        }



        private static void SummitLinkStatusReceived(object sender, UnexpectedLinkStatusEvent statusEvent)
        {
            if (statusEvent.TheLinkStatus.OOR)
            {
                //notify user
                Console.WriteLine("Stimulation engine is out of range!");
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
            int nloops = (int)Math.Floor((double)(packetEstTime - m_prevPacketEstTime) / 65536000);
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
