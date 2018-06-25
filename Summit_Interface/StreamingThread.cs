using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using Medtronic.SummitAPI.Classes;
using Medtronic.SummitAPI.Events;
using Medtronic.TelemetryM;
using Medtronic.NeuroStim.Olympus.DataTypes.Core;
using Medtronic.NeuroStim.Olympus.DataTypes.Sensing;
using Medtronic.NeuroStim.Olympus.Commands;
using Medtronic.NeuroStim.Olympus.DataTypes.PowerManagement;
using Medtronic.NeuroStim.Olympus.DataTypes.Therapy;
using Medtronic.NeuroStim.Olympus.DataTypes.DeviceManagement;

using NetMQ;
using NetMQ.Sockets;

namespace Summit_Interface
{
    //enum of the different tasks for the threads
    enum ThreadType { sense, stim, dataSave };

    //Structure holding all the things we want to pass between threads (Make sure all of these are thread safe!)
    struct ThreadResources
    {
        public INSBuffer TDbuffer { get; set; } //thread-safe buffer holding time-domain data
        public INSBuffer FFTBuffer { get; set; } //thread-safe buffer holding FFT data
        public INSBuffer PWBuffer { get; set; } //thread-safe buffer holding power band data
        public INSBuffer savingBuffer { get; set; } //thread-safe buffer holding data that will be saved to disk
        public SummitSystem summit { get; set; } //summit object for making API calls
        public String saveDataFileName { get; set; } //name of the file to save data to
        public TdSampleRates samplingRate { get; set; } //sampling rate of the time-domain channels (to put in the header of the data file)
        public ThreadsafeFileStream timingLogFile { get; set; } //thread-safe file for writing debug information to
        public INSParameters parameters { get; set; } //configuration parameters read from JSON file. Is read only so is threadsafe
    }

    //Thread class for multithreading the streaming functions to and from Open-Ephys and for saving data to disk
    class StreamingThread
    {
        private Thread m_thread; //thread object
        private bool m_stopped { get; set; } //for stopping the thread
        public ThreadType m_type; //thread function

        //constructor
        public StreamingThread(ThreadType threadtype)
        {
            m_stopped = false;

            m_type = threadtype;

            //set the function that the thread will be running
            switch (m_type)
            {

                case ThreadType.sense:
                    m_thread = new Thread(new ParameterizedThreadStart(SendSense));
                    break;
                case ThreadType.stim:
                    m_thread = new Thread(new ParameterizedThreadStart(GetStim));
                    break;
                case ThreadType.dataSave:
                    m_thread = new Thread(new ParameterizedThreadStart(SaveData));
                    break;
            }
        }


        //starting a thread
        public void StartThread(ref ThreadResources resources)
        {
            m_thread.Start(resources);
        }


        //stopping a thread
        public void StopThread()
        {
            m_stopped = true; //make sure in the thread function that the m_stopped check can always be reached!!!
            m_thread.Join();
        }


        //Code for the the thread passing sense data to Open-Ephys
        public void SendSense(object input)
        {
            //cast to get the shared resources
            ThreadResources resources = (ThreadResources)input;

            //display on console?
            bool dispPackets = resources.parameters.GetParam("NotifyOpenEphysPacketsReceived", typeof(bool));

            using (ResponseSocket senseSocket = new ResponseSocket())
            {
                senseSocket.Bind("tcp://localhost:5555");

                //Wait for data request from Open-Ephys
                string gotMessage;
                byte[] sendMessage;

                while (true)
                {
                    if (m_stopped == true) { Thread.Sleep(500); break; }

                    //listening for messages is blocking for 1000 ms, after which it will check if it should exit thread, and if not, listen again (have this so that this thread isn't infinitely blocking when trying to join)
                    senseSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(1000), out gotMessage);
                    if (gotMessage == null)//not actual message received, just the timeout being hit
                    {
                        continue;
                    }

                    //log time received to timing file
                    string timestamp = DateTime.Now.Ticks.ToString();
                    resources.timingLogFile.WriteLine("1 " + timestamp);

                    //announce that an openEphys packet was requested
                    if (dispPackets)
                    {
                        Console.WriteLine("OpenEphys Packet requested, time Event Called:" + DateTime.Now.Ticks.ToString());
                    }

                    switch (gotMessage)
                    {
                        case "TD":
                            //requested time domain data
                            sendMessage = resources.TDbuffer.getDataByteArray(true);
                            senseSocket.SendFrame(sendMessage, false);
                            break;
                        case "FB":
                            //Told us to flush the buffer
                            resources.TDbuffer.FlushBuffer();
                            sendMessage = new byte[0];
                            senseSocket.SendFrame(sendMessage);
                            break;
                    }
                }
            }
        }


        //Code for the the thread getting decoded stim class from Open-Ephys and sending stim info to the INS
        private void GetStim(object input)
        {
            //cast to get the shared resources
            ThreadResources resources = (ThreadResources)input;

            using (SubscriberSocket stimSocket = new SubscriberSocket())
            {
                stimSocket.Bind("tcp://localhost:12345");
                stimSocket.Subscribe("");

                //initialize variables for summit functions
                APIReturnInfo bufferInfo = new APIReturnInfo();
                double? outBufferDouble;
                int? outBuffer;

                //turn on therapy
                APIReturnInfo returnInfoBuffer;
                returnInfoBuffer = resources.summit.StimChangeTherapyOn();
                Thread.Sleep(500);
                double currentStimAmpFlex = 0;
                double currentStimAmpExt = 0;
                double flexAmp = 0.5;
                double ExtAmp = 0.5;

                if (bufferInfo.RejectCodeType == typeof(MasterRejectCode)
                           && (MasterRejectCode)bufferInfo.RejectCode == MasterRejectCode.ChangeTherapyPor)
                {
                    Console.WriteLine("POR set, resetting...");
                    returnInfoBuffer = SummitUtils.resetPOR(resources.summit);
                    returnInfoBuffer = resources.summit.StimChangeTherapyOn();
                }

                if (returnInfoBuffer.RejectCode != 0)
                {
                    Console.WriteLine("Error during stim init, may not function properly. Error descriptor:" + returnInfoBuffer.Descriptor);
                }

                //wait for data from Open-Ephys
                int stimClass = -2;
                string gotMessage;

                while (true)
                {
                    if (m_stopped == true) { Thread.Sleep(500); break; }

                    //listening for messages is blocking for 1000 ms, after which it will check if it should exit thread, and if not, listen again (have this so that this thread isn't infinitely blocking when trying to join)
                    stimSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(1000), out gotMessage);

                    if (gotMessage == null) //not actual message received, just the timeout being hit
                    {
                        continue;
                    }

                    //log time the message was received to timing file
                    string timestamp = DateTime.Now.Ticks.ToString();
                    resources.timingLogFile.WriteLine("2 " + timestamp);

                    //announce that an openEphys packet was requested
                    Console.WriteLine("Stim packet received, time Event Called:" + DateTime.Now.Ticks.ToString());

                    if (ExtAmp > 0.5)
                    {
                        Console.WriteLine("!Warning! ext amp is: " + ExtAmp.ToString());
                    }
                    if (flexAmp > 0.5)
                    {
                        Console.WriteLine("!Warning! flex amp is: " + flexAmp.ToString());
                    }

                    switch (gotMessage)
                    {
                        case "0":
                            // no gait event, turn off stimulation
                            stimClass = 0;
                            Console.WriteLine("0");

                            if (currentStimAmpFlex != 0)
                            {
                                bufferInfo = resources.summit.StimChangeStepAmp(0, -1 * currentStimAmpFlex, out outBufferDouble);
                                currentStimAmpFlex = 0;
                                if (outBufferDouble == null)
                                {
                                    Console.WriteLine("Error in turning off flexor electrodes! " + bufferInfo.Descriptor);
                                }
                            }

                            if (currentStimAmpExt != 0)
                            {
                                bufferInfo = resources.summit.StimChangeStepAmp(1, -1 * currentStimAmpExt, out outBufferDouble);
                                currentStimAmpExt = 0;
                                if (returnInfoBuffer.RejectCode != 0)
                                {
                                    Console.WriteLine("Error in turning off extensor electrodes! " + bufferInfo.Descriptor);
                                }
                            }
                            break;

                        case "1":
                            stimClass = 1;
                            Console.WriteLine("1");
                            //transition to stance, flexor stim
                            if (currentStimAmpFlex == 0)
                            {
                                returnInfoBuffer = resources.summit.StimChangeStepAmp(0, flexAmp, out outBufferDouble);
                                currentStimAmpFlex += flexAmp;
                            }

                            if (returnInfoBuffer.RejectCode != 0)
                            {
                                Console.WriteLine("Error in turning on flexor electrodes!" + returnInfoBuffer.Descriptor);
                            }
                            break;

                        case "2":
                            stimClass = 2;
                            Console.WriteLine("2");
                            // transition to swing, extensor stim

                            if (currentStimAmpExt == 0)
                            {
                                returnInfoBuffer = resources.summit.StimChangeStepAmp(1, ExtAmp, out outBufferDouble);
                                currentStimAmpExt += ExtAmp;
                            }
                            if (returnInfoBuffer.RejectCode != 0)
                            {
                                Console.WriteLine("Error in turning on extensor electrodes!" + returnInfoBuffer.Descriptor);
                            }
                            break;

                        default:
                            Console.WriteLine("!Error, got message: " + gotMessage);
                            break;

                    }

                    //save stim info to buffer
                    resources.savingBuffer.setStim(stimClass);
                }
            }

        }

        //Code for the the thread saving data to disk
        private void SaveData(object input)
        {
            //cast to get the shared resources
            ThreadResources resources = (ThreadResources)input;

            // save file
            System.IO.StreamWriter saveDataFile = new System.IO.StreamWriter(resources.saveDataFileName + "-Data.txt");

            // sample counter
            int counter = 0;

            //Write some basic information in the header
            saveDataFile.Write("Recording Start Time: " + String.Format("{0:F}", DateTime.Now) + "\r\n");
            switch (resources.samplingRate)
            {
                case TdSampleRates.Sample0250Hz:
                    saveDataFile.Write("Sampling rate: 250 Hz \r\n");
                    break;

                case TdSampleRates.Sample0500Hz:
                    saveDataFile.Write("Sampling rate: 500 Hz \r\n");
                    break;

                case TdSampleRates.Sample1000Hz:
                    saveDataFile.Write("Sampling rate: 1000 Hz \r\n");
                    break;
            }

            //Write column labels
            saveDataFile.Write("SampleNumber \t SenseChannel1 \t SenseChannel2 \t SenseChannel3 \t SenseChannel4 \t StimulationClass \t PacketNumber \t Timestamp \t IsDroppedPacket \r\n");

            //Start saving data
            while (true)
            {
                if (m_stopped == true)
                {
                    saveDataFile.Close();
                    break;
                }

                //save data to file as space delimited values
                int nSamples = resources.savingBuffer.getNumBufferSamples();
                int nChans = resources.savingBuffer.getNumChans();
                double[,] bufferData = resources.savingBuffer.getData(true, true, true, true, true);

                for (int iSample = 0; iSample < nSamples; iSample++)
                {
                    //save sample number since recording started
                    saveDataFile.Write(counter + "\t");
                    counter++;

                    for (int iChan = 0; iChan < nChans + 4; iChan++)
                    {
                        double value = bufferData[iChan, iSample];
                        saveDataFile.Write(value);

                        if (iChan != nChans + 3)
                        {
                            saveDataFile.Write("\t");
                        }
                    }
                    saveDataFile.Write("\r\n");
                }

                Thread.Sleep(100); //save data from buffer every 100 ms
            }
        }


        //

    }
}
