using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Windows.Forms;

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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Summit_Interface
{
    //enum of the different tasks for the threads
    public enum ThreadType { sense, stim, dataSave, myRCS };

    //Structure holding all the things we want to pass between threads (Make sure all of these are thread safe!)
    public struct ThreadResources
    {
        public INSBuffer TDbuffer { get; set; } //thread-safe buffer holding time-domain data
        public INSBuffer FFTBuffer { get; set; } //thread-safe buffer holding FFT data
        public INSBuffer PWBuffer { get; set; } //thread-safe buffer holding power band data
        public INSBuffer savingBuffer { get; set; } //thread-safe buffer holding data that will be saved to disk
        public SummitSystem summit { get; set; } //summit object for making API calls
        public SummitManager summitManager { get; set; } //summit manager object for reconnecting to the INS
        public String saveDataFileName { get; set; } //name of the file to save data to
        public TdSampleRates samplingRate { get; set; } //sampling rate of the time-domain channels (to put in the header of the data file)
        public ThreadsafeFileStream timingLogFile { get; set; } //thread-safe file for writing debug information to
        public INSParameters parameters { get; set; } //configuration parameters read from JSON file. Is read only so is threadsafe
        public bool testMyRCPS { get; set; } //if we are running code in test mode for testing connection to MyRC+S program
    }

    //Thread class for multithreading the streaming functions to and from Open-Ephys and for saving data to disk
    public class StreamingThread
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
                case ThreadType.myRCS:
                    m_thread = new Thread(new ParameterizedThreadStart(MyRCS));
                    m_thread.SetApartmentState(ApartmentState.STA);
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

        public void MyRCS(object input)
        {
            //cast to get the shared resources
            ThreadResources resources = (ThreadResources)input;

            //if we're doing testing, dont actually do anything with the INS
            bool testing = resources.testMyRCPS;

            //option to use custom loaded return message
            bool loadReturnMsg = false; //set to false, instead use a set of hard-coded predefined messages

            //random number generator for test responses
            Random random = new Random();

            using (ResponseSocket myRCSSocket = new ResponseSocket())
            {
                myRCSSocket.Bind("tcp://localhost:5556");

                //load in the JSON schema for the messages
                string schemaFileName;
                if (File.Exists("../../../../JSONFiles/OCD_Schema.json"))
                {
                    schemaFileName = "../../../../JSONFiles/OCD_Schema.json";
                }
                else
                {
                    OpenFileDialog schemaFileDialog = new OpenFileDialog();
                    schemaFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                    schemaFileDialog.FilterIndex = 0;
                    schemaFileDialog.RestoreDirectory = true;
                    schemaFileDialog.Multiselect = false;
                    schemaFileDialog.Title = "Select JSON schema file";
                    if (schemaFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        schemaFileName = schemaFileDialog.FileName;
                    }
                    else
                    {
                        Console.WriteLine("Error: unable to read schema file");
                        Console.WriteLine("Press 'q' to quit.");
                        Console.ReadKey();
                        Thread.Sleep(500);
                        return;
                    }
                }

                JSchema messageSchema;
                using (StreamReader schemaFile = File.OpenText(schemaFileName))
                using (JsonTextReader reader = new JsonTextReader(schemaFile))
                {
                    messageSchema = JSchema.Load(reader);
                }

                //Wait for data request from myRC+S
                string requestMessage;

                while (true)
                {
                    if (m_stopped == true) { Thread.Sleep(500); break; }

                    //listening for messages is blocking for 1000 ms, after which it will check if it should exit thread, and if not, listen again (have this so that this thread isn't infinitely blocking when trying to join)
                    myRCSSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(1000), out requestMessage);
                    if (requestMessage == null)//not actual message received, just the timeout being hit
                    {
                        continue;
                    }

                    //check that the received message validates against the schema
                    JObject requestMsgObj = JObject.Parse(requestMessage);

                    bool msgValid = requestMsgObj.IsValid(messageSchema);

                    if (msgValid)
                    {
                        //deserialize message
                        MyRCSMsg receivedMsg = JsonConvert.DeserializeObject<MyRCSMsg>(requestMessage);

                        //log time received to timing file
                        string timestamp = DateTime.Now.Ticks.ToString();
                        resources.timingLogFile.WriteLine(receivedMsg.message + " " + timestamp);

                        //initialize return message object
                        MyRCSMsg returnMsg = new MyRCSMsg();
                        returnMsg.message = receivedMsg.message;
                        returnMsg.message_type = "result";

                        //determine what message it is
                        switch (receivedMsg.message)
                        {
                            case "battery":
                                if (!testing)
                                {
                                    //read battery level from INS
                                    BatteryStatusResult batteryStatus;
                                    APIReturnInfo commandInfo = resources.summit.ReadBatteryLevel(out batteryStatus);

                                    if (commandInfo.RejectCode == 0)
                                    {
                                        ushort batteryPercentage = ushort.Parse(batteryStatus.BatteryLevelPercent.ToString());
                                        returnMsg.payload.battery_level = batteryPercentage;
                                    }
                                    else
                                    {
                                        parseError(commandInfo, ref returnMsg);
                                    }

                                }
                                else
                                {
                                    //For testing, send back some pre-defined responses
                                    if (!testResponse(receivedMsg.message, messageSchema, loadReturnMsg, myRCSSocket))
                                    {
                                        continue;
                                    }
                                }
                                break;

                            case "sense_status":
                                if (!testing)
                                {
                                    //read sense status from INS
                                    SensingState theSensingState;
                                    APIReturnInfo commandInfo = resources.summit.ReadSensingState(out theSensingState);

                                    if (commandInfo.RejectCode == 0)
                                    {
                                        bool timeDomainSenseOn = theSensingState.State == SenseStates.LfpSense;
                                        returnMsg.payload.sense_on = timeDomainSenseOn;
                                    }
                                    else
                                    {
                                        parseError(commandInfo, ref returnMsg);
                                    }
                                }
                                else
                                {
                                    //For testing, send back some pre-defined responses
                                    if (!testResponse(receivedMsg.message, messageSchema, loadReturnMsg, myRCSSocket))
                                    {
                                        continue;
                                    }
                                }
                                break;

                            case "stim_status":
                                if (!testing)
                                {
                                    //read stim status from INS
                                    GeneralInterrogateData generalInfo;
                                    APIReturnInfo commandInfo = resources.summit.ReadGeneralInfo(out generalInfo);
                                    if (commandInfo.RejectCode == 0)
                                    {
                                        bool stimOn = generalInfo.TherapyStatusData.TherapyStatus == InterrogateTherapyStatusTypes.TherapyActive;
                                        returnMsg.payload.stim_on = stimOn;
                                    }
                                    else
                                    {
                                        parseError(commandInfo, ref returnMsg);
                                    }

                                }
                                else
                                {
                                    //For testing, send back some pre-defined responses
                                    if (!testResponse(receivedMsg.message, messageSchema, loadReturnMsg, myRCSSocket))
                                    {
                                        continue;
                                    }
                                }
                                break;

                            case "sense_on":
                                if (!testing)
                                {
                                    //turn sensing on
                                    APIReturnInfo commandInfo = resources.summit.WriteSensingState(SenseStates.LfpSense | SenseStates.Fft | SenseStates.Power, 0x00);

                                    //send result of command back
                                    if (commandInfo.RejectCode == 0)
                                    {
                                        returnMsg.payload.success = true;
                                    }
                                    else
                                    {
                                        parseError(commandInfo, ref returnMsg);
                                    }
                                }
                                else
                                {
                                    //For testing, send back some pre-defined responses
                                    if (!testResponse(receivedMsg.message, messageSchema, loadReturnMsg, myRCSSocket))
                                    {
                                        continue;
                                    }
                                }
                                break;

                            case "sense_off":
                                if (!testing)
                                {
                                    //turn sensing off
                                    APIReturnInfo commandInfo = resources.summit.WriteSensingState(SenseStates.None, 0);

                                    //send result of command back
                                    if (commandInfo.RejectCode == 0)
                                    {
                                        returnMsg.payload.success = true;
                                    }
                                    else
                                    {
                                        parseError(commandInfo, ref returnMsg);
                                    }
                                }
                                else
                                {
                                    //For testing, send back some pre-defined responses
                                    if (!testResponse(receivedMsg.message, messageSchema, loadReturnMsg, myRCSSocket))
                                    {
                                        continue;
                                    }
                                }
                                break;

                            case "stim_on":
                                if (!testing)
                                {
                                    //turn stim on
                                    APIReturnInfo commandInfo = resources.summit.StimChangeTherapyOn();

                                    //send result of command back
                                    if (commandInfo.RejectCode == 0)
                                    {
                                        returnMsg.payload.success = true;
                                    }
                                    else
                                    {
                                        parseError(commandInfo, ref returnMsg);
                                    }
                                }
                                else
                                {
                                    //For testing, send back some pre-defined responses
                                    if (!testResponse(receivedMsg.message, messageSchema, loadReturnMsg, myRCSSocket))
                                    {
                                        continue;
                                    }
                                }
                                break;

                            case "stim_off":
                                if (!testing)
                                {
                                    //turn stim on
                                    APIReturnInfo commandInfo = resources.summit.StimChangeTherapyOff(true);

                                    //send result of command back
                                    if (commandInfo.RejectCode == 0)
                                    {
                                        returnMsg.payload.success = true;
                                    }
                                    else
                                    {
                                        parseError(commandInfo, ref returnMsg);
                                    }

                                }
                                else
                                {
                                    //For testing, send back some pre-defined responses
                                    if (!testResponse(receivedMsg.message, messageSchema, loadReturnMsg, myRCSSocket))
                                    {
                                        continue;
                                    }
                                }
                                break;

                            case "reconnect":
                                if (!testing)
                                {
                                    SummitSystem tmpSummit = resources.summit;
                                    if (SummitUtils.SummitConnect(resources.summitManager, ref tmpSummit))
                                    {
                                        returnMsg.payload.success = true;
                                    }
                                    else
                                    {
                                        returnMsg.payload.success = false;
                                        returnMsg.payload.error_code = 4;
                                        returnMsg.payload.error_message = "Unable to connect to the INS";
                                    }
                                    resources.summit = tmpSummit;
                                }
                                else
                                {
                                    //For testing, send back some pre-defined responses
                                    if (!testResponse(receivedMsg.message, messageSchema, loadReturnMsg, myRCSSocket))
                                    {
                                        continue;
                                    }
                                }
                                break;

                        }

                        if (!testing)
                        {
                            string responseMsgText = JsonConvert.SerializeObject(returnMsg);
                            JObject responseMsgObj = JObject.Parse(responseMsgText);

                            if (responseMsgObj.IsValid(messageSchema))
                            {
                                myRCSSocket.SendFrame(responseMsgText);
                            }
                            else
                            {
                                Console.WriteLine("Error: response message, does not conform to the schema, not sending response");
                            }
                        }

                    }
                    else
                    {
                        //message not valid, post error on console and don't respond to the request
                        Console.WriteLine("Error: received JSON message from MyRC+S that doesn't conform to schema!");
                        continue;
                    }

                }

                myRCSSocket.Dispose();
            }

            NetMQConfig.Cleanup();

        }

        public void parseError(APIReturnInfo summitInfo, ref MyRCSMsg msg)
        {
            
        }


        public bool testResponse(string responseType, JSchema messageSchema, bool loadResponse, ResponseSocket myRCSSocket)
        {
            //if we aren't given a fixed response already, load in a response from a JSON file
            string responseMsgText;
            if (loadResponse)
            {
                //load in response message
                OpenFileDialog responseFileDialog = new OpenFileDialog();
                responseFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                responseFileDialog.FilterIndex = 0;
                responseFileDialog.RestoreDirectory = true;
                responseFileDialog.Multiselect = false;
                responseFileDialog.Title = "Select JSON response message file for " + responseType;
                string responseFileName;
                if (responseFileDialog.ShowDialog() == DialogResult.OK)
                {
                    responseFileName = responseFileDialog.FileName;
                }
                else
                {
                    Console.WriteLine("Error: unable to read response message file, not sending response");
                    return false;
                }

                responseMsgText = File.ReadAllText(responseFileName);
            }
            else
            {
                //generate a return messge here
                MyRCSMsg returnMsg = new MyRCSMsg();

                // randomly determine if succesfully turned sense on or some error
                returnMsg.message = responseType;
                returnMsg.message_type = "result";

                Random random = new Random();

                // response depends on what the request was
                switch (responseType)
                {
                    case "battery":
                        //generate random value between 0 -100
                        returnMsg.payload.battery_level = (ushort)random.Next(0, 101);
                        break;

                    case "sense_status":
                        //randomly send back sense is on or sense is off
                        if (random.Next(2) == 0)
                        {
                            returnMsg.payload.sense_on = true;
                        }
                        else
                        {
                            returnMsg.payload.sense_on = false;
                        }
                        break;

                    case "stim_status":
                        //randomly send back stim is on or stim is off
                        if (random.Next(2) == 0)
                        {
                            returnMsg.payload.stim_on = true;
                        }
                        else
                        {
                            returnMsg.payload.stim_on = false;
                        }
                        break;

                    case "sense_on":
                    case "sense_off":
                    case "stim_on":
                    case "stim_off":
                    case "reconnect":
                        //for turning stim/sense on/off or reconnecting, randomly send back either success or some error
                        int randInt = random.Next(3);
                        if (randInt == 0)
                        {
                            returnMsg.payload.success = true;
                        }
                        else if (randInt == 1)
                        {
                            returnMsg.payload.success = false;
                            returnMsg.payload.error_code = 1;
                            returnMsg.payload.error_message = "Insufficient Battery";
                        }
                        else if (randInt == 2)
                        {
                            returnMsg.payload.success = false;
                            returnMsg.payload.error_code = 2;
                            returnMsg.payload.error_message = "INS Disconnected";
                        }
                        break;

                }
                
                responseMsgText = JsonConvert.SerializeObject(returnMsg);

            }
           
            JObject testMsgObj = JObject.Parse(responseMsgText);

            if (testMsgObj.IsValid(messageSchema))
            {
                myRCSSocket.SendFrame(responseMsgText);
                return true;
            }
            else
            {
                Console.WriteLine("Error: response message, does not conform to the schema, not sending response");
                return false;
            }

        }

        public class MyRCSMsg
        {
            public string message_type { get; set; }
            public string message { get; set; }
            public Payload payload { get; set; }

            public MyRCSMsg()
            {
                message_type = "";
                message = "";
                payload = new Payload();
            }

            public class Payload
            {
                public bool success { get; set; }
                public UInt16 error_code { get; set; }
                public string error_message { get; set; }
                public UInt16 battery_level { get; set; }
                public bool sense_on { get; set; }
                public bool stim_on { get; set; }
                public SenseInfo sense_config { get; set; }
                public StimInfo stim_config { get; set; }

                public Payload()
                {
                    success = true;
                    error_code = 0;
                    error_message = "";
                    battery_level = 0;
                    sense_on = true;
                    stim_on = true;
                    sense_config = new SenseInfo();
                    stim_config = new StimInfo();
                }
                
                public class SenseInfo
                {
                    public bool time_domain_on { get; set; }
                    public bool FFT_on { get; set; }
                    public bool accel_on { get; set; }
                    public bool powerbands_on { get; set; }
                    public List<UInt16> anodes { get; set; }
                    public List<UInt16> cathodes { get; set; }
                    public List<UInt16> sampling_rates { get; set; }
                    public List<double> highpass_filter { get; set; }
                    public List<UInt16> lowpass_filter1 { get; set; }
                    public List<UInt16> lowpass_filter2 { get; set; }
                    public UInt16 FFT_size { get; set; }
                    public UInt16 FFT_interval { get; set; }
                    public bool FFT_windowing_on { get; set; }
                    public string FFT_window_load { get; set; }
                    public UInt16 FFT_stream_size { get; set; }
                    public UInt16 FFT_stream_offset { get; set; }
                    public List<UInt16> powerband1_lower_cutoff { get; set; }
                    public List<UInt16> powerband1_upper_cutoff { get; set; }
                    public List<UInt16> powerband2_lower_cutoff { get; set; }
                    public List<UInt16> powerband2_upper_cutoff { get; set; }
                    public List<bool> powerband1_enabled { get; set; }
                    public List<bool> powerband2_enabled { get; set; }

                    public SenseInfo()
                    {
                        time_domain_on = false;
                        FFT_on = false;
                        accel_on = false;
                        powerbands_on = false;
                        anodes = new List<UInt16>();
                        cathodes = new List<UInt16>();
                        sampling_rates = new List<UInt16>();
                        highpass_filter = new List<double>();
                        lowpass_filter1 = new List<UInt16>();
                        lowpass_filter2 = new List<UInt16>();
                        FFT_size = 0;
                        FFT_interval = 0;
                        FFT_windowing_on = false;
                        FFT_window_load = "";
                        FFT_stream_size = 0;
                        FFT_stream_offset = 0;
                        powerband1_lower_cutoff = new List<UInt16>();
                        powerband1_upper_cutoff = new List<UInt16>();
                        powerband2_lower_cutoff = new List<UInt16>();
                        powerband2_upper_cutoff = new List<UInt16>();
                        powerband1_enabled = new List<bool>();
                        powerband2_enabled = new List<bool>();
                    }
                }

                public class StimInfo
                {
                    public UInt16 current_group { get; set; }
                    public List<UInt16> number_of_programs { get; set; }
                    public List<List<UInt16>> anodes { get; set; }
                    public List<List<UInt16>> cathodes { get; set; }
                    public List<UInt16> pulsewidth_lower_limit { get; set; }
                    public List<UInt16> pulsewidth_upper_limit { get; set; }
                    public List<List<UInt16>> current_pulsewidth { get; set; }
                    public List<double> frequency_lower_limit { get; set; }
                    public List<double> frequency_upper_limit { get; set; }
                    public List<double> current_frequency { get; set; }
                    public List<List<double>> amplitude_lower_limit { get; set; }
                    public List<List<double>> amplitude_upper_limit { get; set; }
                    public List<List<double>> current_amplitude { get; set; }
                    public List<List<bool>> active_recharge { get; set; }

                    public StimInfo()
                    {
                        current_group = 0;
                        number_of_programs = new List<UInt16>();
                        anodes = new List<List<UInt16>>();
                        cathodes = new List<List<UInt16>>();
                        pulsewidth_lower_limit = new List<UInt16>();
                        pulsewidth_upper_limit = new List<UInt16>();
                        current_pulsewidth = new List<List<UInt16>>();
                        frequency_lower_limit = new List<double>();
                        frequency_upper_limit = new List<double>();
                        current_frequency = new List<double>();
                        amplitude_lower_limit = new List<List<double>>();
                        amplitude_upper_limit = new List<List<double>>();
                        current_amplitude = new List<List<double>>();
                        active_recharge = new List<List<bool>>();
                    }

                }
            }
        }

        //

    }
}
