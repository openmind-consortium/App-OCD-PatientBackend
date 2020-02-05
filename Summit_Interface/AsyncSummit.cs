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
    public class AsyncSummit
    {
        //class members
        public SummitSystem m_summit; //the actual summit system

        //whether or not the SummitSystem has been initialized or not
        private bool m_isInitialized;

        //whether or not the SummitSystem is in the middle of performing an API function call
        private bool m_isPerformingAction; 

        //the name of the API function being called, if it is in the middle of a call
        private string m_currentAPICall;

        //time the call was started
        private DateTime m_APICallStartTime;



        //methods
        public void setSummit(ref SummitSystem theSummit)
        {
            m_summit = theSummit;
            m_isInitialized = true;
        }


    }
}
