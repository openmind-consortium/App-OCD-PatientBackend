using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SummitPythonInterface
{
    public class StimParams
    {
        public int Group { get; set; }
        public IList<int> PW { get; set; }
        public int DurationInMilliseconds { get; set; }
        public IList<double> Amplitude { get; set; }
        public double Frequency { get; set; }
        public bool ForceQuit { get; set; }
        public bool AddReverse { get; set; }
    }
}
