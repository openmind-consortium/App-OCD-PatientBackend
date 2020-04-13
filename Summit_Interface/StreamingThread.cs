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
    public enum ThreadType { sense, dataSave, myRCpS };

    //Structure holding all the things we want to pass between threads (Make sure all of these are thread safe!)
    public struct ThreadResources
    {
        public INSBuffer TDbuffer { get; set; } //thread-safe buffer holding time-domain data
        public INSBuffer FFTBuffer { get; set; } //thread-safe buffer holding FFT data
        public INSBuffer PWBuffer { get; set; } //thread-safe buffer holding power band data
        public INSBuffer savingBuffer { get; set; } //thread-safe buffer holding data that will be saved to disk
        public SummitSystemWrapper summitWrapper { get; set; } //summit object for making API calls (put in a wrapper to make it nullable)
        public SummitManager summitManager { get; set; } //summit manager object for reconnecting to the INS
        public String saveDataFileName { get; set; } //name of the file to save data to
        public TdSampleRates samplingRate { get; set; } //sampling rate of the time-domain channels (to put in the header of the data file)
        public bool enableTimeSync { get; set; } //whether to use the summit API time-sync latentency estimates
        public ThreadsafeFileStream timingLogFile { get; set; } //thread-safe file for writing debug information to
        public INSParameters parameters { get; set; } //configuration parameters read from JSON file. Is read only so is threadsafe
        public bool testMyRCPS { get; set; } //if we are running code in test mode for testing connection to MyRC+S program
        public endProgramWrapper endProgram { get; set; } //to tell main program to quit
    }

    public class SummitSystemWrapper
    {
        public bool isInitialized;
        public SummitSystem summit;

        public SummitSystemWrapper()
        {
            isInitialized = false;
            summit = null;
        }

        public void setSummit(ref SummitSystem theSummit)
        {
            summit = theSummit;
            isInitialized = true;
        }
    }

    public class endProgramWrapper
    {
        public bool end;

        public endProgramWrapper()
        {
            end = false;
        }
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
                case ThreadType.dataSave:
                    m_thread = new Thread(new ParameterizedThreadStart(SaveData));
                    break;
                case ThreadType.myRCpS:
                    m_thread = new Thread(new ParameterizedThreadStart(MyRCpS));
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

        public void MyRCpS(object input)
        {
            //cast to get the shared resources
            ThreadResources resources = (ThreadResources)input;

            //if we're doing testing, dont actually do anything with the INS
            bool testing = resources.testMyRCPS;

            //option to use custom loaded return message
            bool loadReturnMsg = false; //set to false, instead use a set of hard-coded predefined messages

            //random number generator for test responses
            Random random = new Random();

            //Thread.Sleep(10000);
            //resources.endProgram.end = true;
            
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

                    //create response
                    MyRCSMsg returnMsg = new MyRCSMsg();

                    //check that the received message validates against the schema
                    JObject requestMsgObj;
                    bool msgParsed = true;
                    try
                    {
                        requestMsgObj = JObject.Parse(requestMessage);
                    }
                    catch
                    {
                        Console.WriteLine("Received a msg from MyRCpS program, but was unable to parse JSON");
                        msgParsed = false;
                        requestMsgObj = null;
                    }

                    bool msgValid;
                    if (msgParsed)
                    {
                        msgValid = requestMsgObj.IsValid(messageSchema);
                    }
                    else
                    {
                        msgValid = false;
                    }

                    if (msgValid)
                    {
                        //deserialize message
                        MyRCSMsg receivedMsg = JsonConvert.DeserializeObject<MyRCSMsg>(requestMessage);

                        //log time received to timing file
                        string timestamp = DateTime.Now.Ticks.ToString();
                        resources.timingLogFile.WriteLine(receivedMsg.message + " " + timestamp);

                        //initialize return message object
                        returnMsg.message = receivedMsg.message;
                        returnMsg.message_type = "result";

                        //first make sure INS/CTM is connected
                        if (!resources.summitWrapper.isInitialized)
                        {
                            returnMsg.payload.success = false;
                            returnMsg.payload.error_code = 3;
                            returnMsg.payload.error_message = "Initial CTM connection not established";
                        }
                        else
                        {
                            //determine what message it is
                            switch (receivedMsg.message)
                            {
                                case "battery":
                                    if (!testing)
                                    {
                                        //read battery level from INS
                                        BatteryStatusResult batteryStatus;
                                        APIReturnInfo commandInfo = resources.summitWrapper.summit.ReadBatteryLevel(out batteryStatus);

                                        if (commandInfo.RejectCode == 0)
                                        {
                                            ushort batteryPercentage = ushort.Parse(batteryStatus.BatteryLevelPercent.ToString());
                                            returnMsg.payload.battery_level = batteryPercentage;
                                        }
                                        else
                                        {
                                            parseError(commandInfo, 0, ref returnMsg);
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

                                case "device_info":
                                    if (!testing)
                                    {
                                        //get info from the INS
                                        APIReturnInfo commandInfo = SummitUtils.QueryDeviceStatus(
                                            resources.summitWrapper.summit, out StreamingThread.MyRCSMsg.Payload payload, out int parseErrorCode);

                                        if (commandInfo.RejectCode == 0 && parseErrorCode == 0)
                                        {
                                            returnMsg.payload = payload;
                                        }
                                        else
                                        {
                                            parseError(commandInfo, parseErrorCode, ref returnMsg);
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
                                        APIReturnInfo commandInfo = resources.summitWrapper.summit.WriteSensingState(SenseStates.LfpSense | SenseStates.Fft | SenseStates.Power, 0x00);
                                        commandInfo = resources.summitWrapper.summit.WriteSensingEnableStreams(true, true, true, false, false, true, resources.enableTimeSync, false);

                                        //send result of command back
                                        if (commandInfo.RejectCode == 0)
                                        {
                                            returnMsg.payload.success = true;
                                        }
                                        else
                                        {
                                            parseError(commandInfo, 0, ref returnMsg);
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
                                        APIReturnInfo commandInfo = resources.summitWrapper.summit.WriteSensingDisableStreams(true, true, true, false, false, true, resources.enableTimeSync, false);

                                        //send result of command back
                                        if (commandInfo.RejectCode == 0)
                                        {
                                            returnMsg.payload.success = true;
                                        }
                                        else
                                        {
                                            parseError(commandInfo, 0, ref returnMsg);
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

                                case "quit":
                                    //tell main program to quit
                                    resources.endProgram.end = true;
                                    break;

                            }
                        }

                    }
                    else if (msgParsed)
                    {
                        //message was parsed does not conform to schema
                        Console.WriteLine("Error: received JSON message from MyRC+S that doesn't conform to schema!");
                        returnMsg.message = "device_info";
                        returnMsg.payload.success = false;
                        returnMsg.payload.error_code = 2;
                        returnMsg.payload.error_message = "received JSON message does not conform to schema";
                    } else
                    {
                        //message wasn't even able to be parsed
                        returnMsg.message = "device_info";
                        returnMsg.payload.success = false;
                        returnMsg.payload.error_code = 1;
                        returnMsg.payload.error_message = "Unable to parse JSON message";
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

                myRCSSocket.Dispose();
            }

            NetMQConfig.Cleanup();

        }

        public void parseError(APIReturnInfo summitInfo, int parseErrorCode, ref MyRCSMsg msg)
        {

            msg.payload.success = false;

            bool errorParsed = false;

            //first see if the error was happening in the SummitInterface side rather than the hardward side
            if (parseErrorCode != 0)
            {

                switch (parseErrorCode)
                {
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                    case 5:
                        // This range are error codes from QueryDeviceStatus()
                        // 1 - Error in parsing Enums
                        // 2 - # of power bands != # of time domain channels 
                        // 3 - found two anodes for a stim program
                        // 4 - found two cathodes for a stim program
                        // 5 - couldn't find anode or cathode for a stim program 
                        msg.payload.error_code = 10;
                        msg.payload.error_message = "Error in parsing device info";
                        errorParsed = true;
                        break;

                    case 6:
                    case 7:
                    case 8:
                        // This range are error codes from ChangeStimParameter()
                        // 6 - Error parsing target group
                        // 7 - target program not within 0-3
                        // 8 - parameter is not "frequency", "amplitude", or "pulse_width"
                        msg.payload.error_code = 11;
                        msg.payload.error_message = "Error in parsing stim change commands";
                        errorParsed = true;
                        break;

                }
                return;

            }

            //error originating from the API
            if (summitInfo.RejectCodeType == typeof(Medtronic.SummitAPI.Classes.APIRejectCodes))
            {
                switch (summitInfo.RejectCode)
                {
                    case 11:
                        //Connected to CTM but hasn't done initial connection to INS yet
                        msg.payload.error_code = 4;
                        msg.payload.error_message = "Initial INS connection not established";
                        errorParsed = true;
                        break;
                }
            }

            //error originating from CTM
            if (summitInfo.RejectCodeType == typeof(Medtronic.TelemetryM.CtmProtocol.Commands.CommandResponseCodes))
            {
                switch (summitInfo.RejectCode)
                {
                    case 131:
                    case 132:
                        //CTM still connected, INS disconnected
                        msg.payload.error_code = 5;
                        msg.payload.error_message = "INS connection lost";
                        errorParsed = true;
                        break;
                }
            }

            //error originating from CTM (of a different sort?)
            if (summitInfo.RejectCodeType == typeof(Medtronic.TelemetryM.InstrumentReturnCode))
            {
                switch (summitInfo.RejectCode)
                {
                    case 7:
                        //disconnected through timeout (usually only happens when debugging), not sure which is disconnected

                        break;

                    case 9:
                        //CTM disconnected (probably, technically it's a CTM timeout)
                        msg.payload.error_code = 6;
                        msg.payload.error_message = "CTM connection lost";
                        errorParsed = true;
                        break;
                }
            }

            //error originating from INS?
            if (summitInfo.RejectCodeType == typeof(Medtronic.NeuroStim.Olympus.Commands.MasterRejectCode))
            {
                switch (summitInfo.RejectCode)
                {
                    case 55553:
                        //battery too low to turn sense on
                        msg.payload.error_code = 7;
                        msg.payload.error_message = "Battery too low for sense";
                        errorParsed = true;
                        break;

                }
            }

            if (errorParsed == false)
            {
                msg.payload.error_code = 9;
                msg.payload.error_message = "Unable to determine what the error was";
            }


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

                    case "device_info":
                        //just send back a fixed defined payload since there's too many 
                        //variables to generate a random value for all of them
                        returnMsg.payload.battery_level = (ushort)random.Next(0, 101);
                        if (random.Next(2) == 0)
                        {
                            returnMsg.payload.sense_on = true;
                        }
                        else
                        {
                            returnMsg.payload.sense_on = false;
                        }
                        if (random.Next(2) == 0)
                        {
                            returnMsg.payload.stim_on = true;
                        }
                        else
                        {
                            returnMsg.payload.stim_on = false;
                        }

                        returnMsg.payload.sense_config.time_domain_on = true;
                        returnMsg.payload.sense_config.FFT_on = true;
                        returnMsg.payload.sense_config.accel_on = true;
                        returnMsg.payload.sense_config.powerbands_on = true;
                        returnMsg.payload.sense_config.anodes = new List<UInt16>() { 0, 2, 8, 10 };
                        returnMsg.payload.sense_config.cathodes = new List<UInt16>() { 1, 3, 9, 11 };
                        returnMsg.payload.sense_config.sampling_rates = new List<UInt16>() { 500, 500, 500, 500 };
                        returnMsg.payload.sense_config.highpass_filter = new List<double>() { 0.85, 0.85, 0.85, 0.85 };
                        returnMsg.payload.sense_config.lowpass_filter1 = new List<UInt16>() { 450, 450, 450, 450 };
                        returnMsg.payload.sense_config.lowpass_filter2 = new List<UInt16>() { 1700, 1700, 1700, 1700 };
                        returnMsg.payload.sense_config.FFT_size = 1024;
                        returnMsg.payload.sense_config.FFT_interval = 500;
                        returnMsg.payload.sense_config.FFT_windowing_on = true;
                        returnMsg.payload.sense_config.FFT_window_load = "Hann100";
                        returnMsg.payload.sense_config.FFT_stream_size = 0;
                        returnMsg.payload.sense_config.FFT_stream_offset = 0;
                        returnMsg.payload.sense_config.powerband1_lower_cutoff = new List<UInt16>() { 10, 20, 30, 40 };
                        returnMsg.payload.sense_config.powerband1_upper_cutoff = new List<UInt16>() { 20, 30, 40, 50 };
                        returnMsg.payload.sense_config.powerband2_lower_cutoff = new List<UInt16>() { 60, 70, 80, 90 };
                        returnMsg.payload.sense_config.powerband2_upper_cutoff = new List<UInt16>() { 70, 80, 90, 100 };
                        returnMsg.payload.sense_config.powerband1_enabled = new List<bool>() { true, false, true, false };
                        returnMsg.payload.sense_config.powerband2_enabled = new List<bool>() { false, true, false, true };

                        returnMsg.payload.stim_config.current_group = 0;
                        returnMsg.payload.stim_config.number_of_programs = new List<UInt16>() { 4, 2, 0, 0 };
                        returnMsg.payload.stim_config.anodes = new List<List<UInt16>>() {
                            new List<UInt16>() { 0, 1, 2, 2 }, new List<UInt16>() { 13, 14 }, new List<UInt16>(), new List<UInt16>() };
                        returnMsg.payload.stim_config.cathodes = new List<List<UInt16>>() {
                            new List<UInt16>() { 16, 16, 16, 3 }, new List<UInt16>() { 14, 16 }, new List<UInt16>(), new List<UInt16>() };
                        returnMsg.payload.stim_config.pulsewidth_lower_limit = new List<UInt16>() { 0, 0, 0, 0 };
                        returnMsg.payload.stim_config.pulsewidth_upper_limit = new List<UInt16>() { 150, 150, 150, 150 };
                        returnMsg.payload.stim_config.current_pulsewidth = new List<List<UInt16>>() {
                            new List<UInt16>() { 80, 100, 80, 100 }, new List<UInt16>() { 120, 120 }, new List<UInt16>(), new List<UInt16>() };
                        returnMsg.payload.stim_config.frequency_lower_limit = new List<double>() { 0, 0, 0, 0 };
                        returnMsg.payload.stim_config.frequency_upper_limit = new List<double>() { 80, 80, 80, 80 };
                        returnMsg.payload.stim_config.current_frequency = new List<double>() { 10, 20, 30, 40 };
                        returnMsg.payload.stim_config.amplitude_lower_limit = new List<List<double>>() {
                            new List<double>() { 0, 0, 0, 0 }, new List<double>() { 0, 0 }, new List<double>(), new List<double>() };
                        returnMsg.payload.stim_config.amplitude_upper_limit = new List<List<double>>() {
                            new List<double>() { 3, 3, 3, 3 }, new List<double>() { 4, 4 }, new List<double>(), new List<double>() };
                        returnMsg.payload.stim_config.current_amplitude = new List<List<double>>() {
                            new List<double>() { 1, 1.5, 2, 2.5 }, new List<double>() { 3, 0 }, new List<double>(), new List<double>() };
                        returnMsg.payload.stim_config.active_recharge = new List<List<bool>>() {
                            new List<bool>() { true, true, true, true }, new List<bool>() { false, false }, new List<bool>(), new List<bool>() };

                        break;

                    case "sense_on":
                    case "sense_off":
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
                public double new_value { get; set; }
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
                    stim_on = false;
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
