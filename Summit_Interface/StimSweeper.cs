using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Medtronic.SummitAPI.Classes;
using Medtronic.NeuroStim.Olympus.DataTypes.Therapy;
using Medtronic.NeuroStim.Olympus.Commands;

namespace Summit_Interface
{

    //Class that allows us to iterate across any list of amplitudes, pulse widths, and frequencies in any defined order (used for sweeping across stim parameters)
    public class StimSweeper
    {

        public struct SweepParameters
        {
            public int ampOrder; //where in the order to do the Amp values
            public int pulseWidthOrder; //where in the order to do the pulse width values
            public int freqOrder; //where in the order to do the frequency values

            public List<int> paramOrder; //list of the parameter orders
            public List<int> paramNumValues; //the number of values to sweep across for each parameter, must be in the same order as paramOrder
            public List<int> paramPauseDurationMilliSeconds; //how long to pause between sweeps for each parameter, must be in the same order as paramOrder

            public List<double> ampValues; //stimulation amplitudes to sweep across
            public List<int> pulseWidthValues; //stimulation pulse widths to sweep across
            public List<double> freqValues; //stimulation frequencies to sweep across

            public int permutationDuration; //how long to do the stimulation for each permuation of parameters
        }

        private SweepParameters m_sweepParameters;

        private double? m_currentAmp;
        private int? m_currentPulseWidth;
        private double? m_currentFreq;
        private double m_prePauseAmp;
        private bool m_stimPaused;

        private SummitSystem m_summit;
        private APIReturnInfo m_summitInfo;
        private GroupNumber m_groupNum;

        private bool m_stopSweep;
        public bool m_stimOutOfRangeFlag { get; set; }
        private bool m_sweepFinished;
        private string m_quitKey;


        //constructor
        public StimSweeper(SweepParameters parameters, SummitSystem theSummit, GroupNumber groupNum, string quitKey)
        {
            //assign all values
            m_sweepParameters = parameters;

            m_currentAmp = m_sweepParameters.ampValues[0];
            m_currentPulseWidth = m_sweepParameters.pulseWidthValues[0];
            m_currentFreq = m_sweepParameters.freqValues[0];

            m_stimPaused = false;
            m_summit = theSummit;
            m_groupNum = groupNum;

            m_stopSweep = false;
            m_stimOutOfRangeFlag = false ;
            m_sweepFinished = true;
            m_quitKey = quitKey;
        }


        /// <summary>
        /// Sweep through all the indicated stimulation amplitudes, pulse widths, and frequencies. If stimulation engine failure flag is raised, or if user
        /// pushes quit button, the sweep is aborted.
        /// </summary>
        public bool Sweep()
        {
            bool sweepAborted = false;

            //go through each parameter, put thier number of values, stim stop, and pause durations in the right order
            List<int> numValues = new List<int>() { 0, 0, 0 };
            List<int> pauseDurations = new List<int>() { 0, 0, 0 };

            List<int> maxIters = new List<int>(2);
            for (int iLevel = 0; iLevel < m_sweepParameters.paramOrder.Count; iLevel++)
            {
                numValues[m_sweepParameters.paramOrder[iLevel]] = m_sweepParameters.paramNumValues[iLevel];
                pauseDurations[m_sweepParameters.paramOrder[iLevel]] = m_sweepParameters.paramPauseDurationMilliSeconds[iLevel];
            }

            //initialize current values
            if(!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
            {
                sweepAborted = true;
                return sweepAborted;
            }

            //bring pulse width and freq to starting values:
            if (m_currentAmp!=0)
            {
                //amp should be 0 so there will be no stimulation when setting pw and freq. Set to 0 if it's not.
                Console.WriteLine(String.Format("Sweep initialzation amplitude should be 0, but it's {0}! Cannot do stim sweep", m_currentAmp));
                return sweepAborted = true;
            }

            m_summitInfo = m_summit.StimChangeTherapyOn();
            // Reset POR if set
            if (m_summitInfo.RejectCodeType == typeof(MasterRejectCode)
                && (MasterRejectCode)m_summitInfo.RejectCode == MasterRejectCode.ChangeTherapyPor)
            {
                // Inform user
                Console.WriteLine("POR set, resetting...");
                // Reset POR
                m_summitInfo = SummitUtils.resetPOR(m_summit);
            }
            Thread.Sleep(1000);
            try
            {
                ResetSweep();
            }
            catch
            {
                sweepAborted = true;
                return sweepAborted;
            }

            //initialize a counter to 0's for the number of values we've already sweeped across for each parameter
            List<int> iterCounter = new List<int> { 0, 0, 0 };
            int iParameter, iAmp = 0, iPW = 0, iFreq = 0;

            //make a listener thread to check for terminations
            Thread stopListenerThread = new Thread(StopSweepListener);

            //start stimulation by bringing amp to first value
            m_sweepFinished = false;
            m_summitInfo = m_summit.StimChangeStepAmp(0, m_sweepParameters.ampValues[0], out m_currentAmp);
            if (m_summitInfo.RejectCode != 0)
            {
                Console.WriteLine("Error when increasing stimulation amplitude to " + m_sweepParameters.ampValues[iAmp] + "mA. Error descriptor:" + m_summitInfo.Descriptor);
                if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                {
                    sweepAborted = true;
                    return sweepAborted;
                }
            }
            Console.WriteLine("Sweep started at: " + m_currentAmp
                        + "mA , " + m_currentPulseWidth
                        + "us pulse width, and " + m_currentFreq + " Hz");
            stopListenerThread.Start();

            Thread.Sleep(m_sweepParameters.permutationDuration);

            //now do iteration by increasing the counter for each parameter (until all the parameter's value's have all been iterated across)
            while (iterCounter[0] < (numValues[0] - 1) || iterCounter[1] < (numValues[1] - 1) || iterCounter[2] < (numValues[2] - 1))
            {

                //First do check here to see if the sweep has been stopped (either quit key has been pressed or stim engine out of range flag has been set)
                if (m_stopSweep)
                {
                    Console.WriteLine("Stimulation Sweep Aborted");
                    sweepAborted = true;
                    break;
                }

                if (iterCounter[0] < (numValues[0] - 1))
                {
                    try
                    {
                        //we're at innermost parameter
                        IntraSweepPause(pauseDurations[0]);
                        iterCounter[0]++;
                        iParameter = 0;
                    }
                    catch
                    {
                        sweepAborted = true;
                        return sweepAborted;
                    }

                }
                else if (iterCounter[1] < (numValues[1] - 1))
                {
                    //we're at the middle parameter
                    try
                    {
                        IntraSweepPause(pauseDurations[1]);
                        iterCounter[1]++;
                        iParameter = 1;

                        //reset innermost parameter
                        ResetParameter(0, ref iAmp, ref iPW, ref iFreq);
                        iterCounter[0] = 0;
                    }
                    catch
                    {
                        sweepAborted = true;
                        return sweepAborted;
                    }
                    Console.WriteLine();
                }
                else
                {
                    try
                    {
                        //we're at the outermost parameter
                        IntraSweepPause(pauseDurations[2]);
                        iterCounter[2]++;
                        iParameter = 2;

                        //reset innermost and middle parameter
                        ResetParameter(0, ref iAmp, ref iPW, ref iFreq);
                        ResetParameter(1, ref iAmp, ref iPW, ref iFreq);
                        iterCounter[0] = 0;
                        iterCounter[1] = 0;
                    }
                    catch
                    {
                        sweepAborted = true;
                        return sweepAborted;
                    }
                    Console.WriteLine();
                }


                // increment the parameter we're at in the sweep
                if (iParameter == m_sweepParameters.ampOrder)
                {
                    //increase stim amp
                    iAmp++;

                    m_summitInfo = m_summit.StimChangeStepAmp(0, Math.Round(m_sweepParameters.ampValues[iAmp] - m_currentAmp.Value, 1), out m_currentAmp);
                    if (m_summitInfo.RejectCode != 0)
                    {
                        Console.WriteLine("Error when increasing stimulation amplitude to " + m_sweepParameters.ampValues[iAmp] + "mA. Error descriptor:" + m_summitInfo.Descriptor);
                        if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                        {
                            sweepAborted = true;
                            return sweepAborted;
                        }
                    }

                    Console.WriteLine("Increased amplitude to " + m_currentAmp + "mA");

                    m_stimPaused = false;
                    Thread.Sleep(m_sweepParameters.permutationDuration);

                }
                else if (iParameter == m_sweepParameters.pulseWidthOrder)
                {
                    //increase pulse duration
                    iPW++;
                    m_summitInfo = m_summit.StimChangeStepPW(0, m_sweepParameters.pulseWidthValues[iPW] - m_currentPulseWidth.Value, out m_currentPulseWidth);

                    if (m_summitInfo.RejectCode != 0)
                    {
                        Console.WriteLine("Error when increasing stimulation pulse width to " + m_sweepParameters.pulseWidthValues[iPW] + "us. Error descriptor:" + m_summitInfo.Descriptor);
                        if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                        {
                            sweepAborted = true;
                            return sweepAborted;
                        }
                    }

                    Console.WriteLine("Increased pulse width to " + m_currentPulseWidth + "us");

                    //bring stimulation back, if it was turned off for pause
                    if (m_stimPaused)
                    {
                        m_summitInfo = m_summit.StimChangeStepAmp(0, m_prePauseAmp, out m_currentAmp);

                        if (m_summitInfo.RejectCode != 0)
                        {
                            Console.WriteLine("Error when increasing stimulation amplitude back after pulse width increase. Error descriptor:" + m_summitInfo.Descriptor);
                            if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                            {
                                sweepAborted = true;
                                return sweepAborted;
                            }
                        }

                        m_stimPaused = false;
                    }
                    Thread.Sleep(m_sweepParameters.permutationDuration);

                }
                else if (iParameter == m_sweepParameters.freqOrder)
                {
                    //increase frequency
                    iFreq++;

                    m_summitInfo = m_summit.StimChangeStepFrequency(Math.Round(m_sweepParameters.freqValues[iFreq] - m_currentFreq.Value, 1), false, out m_currentFreq);

                    if (m_summitInfo.RejectCode != 0)
                    {
                        Console.WriteLine("Error when increasing stimulation frequency to " + m_sweepParameters.freqValues[iFreq] + "Hz. Error descriptor:" + m_summitInfo.Descriptor);
                        if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                        {
                            sweepAborted = true;
                            return sweepAborted;
                        }
                    }

                    Console.WriteLine("Increased frequency to " + m_currentFreq + "Hz");

                    //bring stimulation back, if it was turned off for pause
                    if (m_stimPaused)
                    {
                        m_summitInfo = m_summit.StimChangeStepAmp(0, m_prePauseAmp, out m_currentAmp);

                        if (m_summitInfo.RejectCode != 0)
                        {
                            Console.WriteLine("Error when increasing stimulation amplitude back after frequency increase. Error descriptor:" + m_summitInfo.Descriptor); 
                            if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                            {
                                sweepAborted = true;
                                return sweepAborted;
                            }
                        }

                        m_stimPaused = false;
                    }
                    Thread.Sleep(m_sweepParameters.permutationDuration);
                }

                //just call some function to interragate INS to allow any OOR info to arrive (add 100 ms sleep to all time for setting the abort flags)
                TherapyGroup groupInfo = new TherapyGroup();
                m_summit.ReadStimGroup(m_groupNum, out groupInfo);
                Thread.Sleep(100);

            }

            //ok sweep finished, reset all parametrs and flags, and join listener thread
            m_stimOutOfRangeFlag = false;
            try
            {
                ResetSweep();
            }
            catch
            {
                sweepAborted = true;
                return sweepAborted;
            }

            //turn therapy off
            m_summit.StimChangeTherapyOff(false);
            m_sweepFinished = true;
            stopListenerThread.Join();
            m_stopSweep = false;
            return sweepAborted;

        }


        /// <summary>
        /// Function to bring the inputted parameter back to the first value
        /// </summary>
        /// <param name="iParameter">should be either m_ampOrder, m_pulseWidthOrder, or m_freqOrder to tell function which parameter to reset</param>
        /// <param name="iAmp">reference to the amplitude index in the sweep (which will be reset if iParameter is m_ampOrder, otherwise won't be changed)</param>
        /// <param name="iPW">reference to the pulse width index in the sweep (which will be reset if iParameter is m_pulseWidthOrder, otherwise won't be changed)</param>
        /// <param name="iFreq">reference to the frequency index in the sweep (which will be reset if iParameter is m_freqOrder, otherwise won't be changed)</param>
        public void ResetParameter(int iParameter, ref int iAmp, ref int iPW, ref int iFreq)
        {
            if (iParameter == m_sweepParameters.ampOrder)
            {
                //decrease stim
                iAmp=0;

                if (m_stimPaused)
                {
                    //should set pre-pause Value instead
                    m_prePauseAmp = m_sweepParameters.ampValues[0];
                }
                else
                {
                    //move stim down to first value
                    if (m_sweepParameters.ampValues[0] - m_currentAmp.Value != 0)
                    {
                        double? resultAmp;
                        m_summitInfo = m_summit.StimChangeStepAmp(0, Math.Round(m_sweepParameters.ampValues[0] - m_currentAmp.Value, 1), out resultAmp);
                        if (m_summitInfo.RejectCode != 0)
                        {
                            Console.WriteLine("Error when decreasing stimulation amplitude to " + m_sweepParameters.ampValues[0] + "mA. Recommend restaring program. Error descriptor:" + m_summitInfo.Descriptor);
                            if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                            {
                                throw new Exception();
                            }
                        }
                        else
                        {
                            m_currentAmp = resultAmp;
                            Console.WriteLine("Decreased Amplitude to " + m_currentAmp + "mA");
                        }
                    }
                }
            }
            else if (iParameter == m_sweepParameters.pulseWidthOrder)
            {
                //decrease pulse width back to first value if needed
                iPW = 0;
                if (m_sweepParameters.pulseWidthValues[0] - m_currentPulseWidth.Value != 0)
                {
                    int? resultPulseWidth;
                    m_summitInfo = m_summit.StimChangeStepPW(0, m_sweepParameters.pulseWidthValues[0] - m_currentPulseWidth.Value, out resultPulseWidth);

                    if (m_summitInfo.RejectCode != 0)
                    {
                        Console.WriteLine("Error when decreasing stimulation pulse width to " + m_sweepParameters.pulseWidthValues[0] + "us. Recommend restaring program. Error descriptor:" + m_summitInfo.Descriptor);
                        if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                        {
                            throw new Exception();
                        }
                    }
                    else
                    {
                        m_currentPulseWidth = resultPulseWidth;
                        Console.WriteLine("Decreased pulse width to " + m_currentPulseWidth + "us");
                    }

                }
            }
            else if (iParameter == m_sweepParameters.freqOrder)
            {
                //change frequency to first value if needed
                iFreq = 0;
                if (m_sweepParameters.freqValues[0] - m_currentFreq.Value != 0)
                {
                    double? resultFreq;
                    m_summitInfo = m_summit.StimChangeStepFrequency(Math.Round(m_sweepParameters.freqValues[0] - m_currentFreq.Value, 1), false, out resultFreq);

                    if (m_summitInfo.RejectCode != 0)
                    {
                        Console.WriteLine("Error when decreasing stimulation frequency to " + m_sweepParameters.freqValues[0] + "Hz. Recommend restaring program. Error descriptor:" + m_summitInfo.Descriptor);
                        if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                        {
                            throw new Exception("");
                        }
                    }
                    else
                    {
                        m_currentFreq = resultFreq;
                        Console.WriteLine("Decreased frequency to " + m_currentFreq + "Hz");
                    }
                }
            }
        }

        /// <summary>
        /// function to pause stimulation by changing amplitude to 0 (note that there is still some residual stimulation, that's just the way the hardware is)
        /// </summary>
        /// <param name="pauseDuration">How long to pause the program in milliseconds</param>
        public void IntraSweepPause(int pauseDuration)
        {
            if (pauseDuration != 0)
            {
                m_stimPaused = true;

                //"turn off" stimulation by bringing amp to 0
                m_prePauseAmp = m_currentAmp.Value;
                m_summitInfo = m_summit.StimChangeStepAmp(0, Math.Round(m_currentAmp.Value * -1, 2), out m_currentAmp);
                if (m_summitInfo.RejectCode != 0)
                {
                    Console.WriteLine("Error when bringing stim amplitude to zero. Error descriptor:" + m_summitInfo.Descriptor);
                    if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                    {
                        throw new Exception();
                    }
                }

                //pause for the specified amount of time
                Thread.Sleep(pauseDuration);
            }
        }


        /// <summary>
        /// reset stimulation sweep by bringing all values back to starting values
        /// </summary>
        public void ResetSweep()
        {
            //set parameters back to initial values
            int iAmp=0, iPulseWidth=0, iFreq=0; //don't care about these
            TherapyGroup groupInfo;

            //To be certain current values are accurate, get current values from INS
            if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
            {
                throw new Exception();
            }
            m_stimPaused = false;

            //now reset the values to the start values
            m_summitInfo = m_summit.StimChangeStepAmp(0, Math.Round(-1 * m_currentAmp.Value, 1), out m_currentAmp);
            if (m_summitInfo.RejectCode != 0)
            {
                Console.WriteLine("Error when bringing stim amplitude to zero at end of sweep. Recommend restart program, as no more sweeps can be started. Error descriptor:" + m_summitInfo.Descriptor);
                if (!SummitUtils.CheckCurrentStimParameters(m_summit, m_groupNum, 0, out m_currentAmp, out m_currentPulseWidth, out m_currentFreq))
                {
                    throw new Exception();
                }
            }

            int periodMS = (int)Math.Ceiling(1000 / m_currentFreq.Value);
            Thread.Sleep(periodMS);
            try
            {
                ResetParameter(m_sweepParameters.freqOrder, ref iAmp, ref iPulseWidth, ref iFreq);
                //get rate period of the current freq
                periodMS = (int)Math.Ceiling(1000 / m_currentFreq.Value);
                Thread.Sleep(periodMS);
                ResetParameter(m_sweepParameters.pulseWidthOrder, ref iAmp, ref iPulseWidth, ref iFreq);
            }
            catch
            {
                throw;
            }

            Thread.Sleep(periodMS);
        }


        /// <summary>
        /// Function run by the listener thread to check if the stimulation engine was unable to supply the correct voltage, or if the user pressed the stop key.
        /// </summary>
        public void StopSweepListener()
        {
            do
            {
                while (!Console.KeyAvailable)
                {
                    //sweep was finished, just join this thread
                    if (m_sweepFinished)
                    {
                        return;
                    }

                    //stim engine out of range, need to abort sweep
                    if (m_stimOutOfRangeFlag)
                    {
                        m_stopSweep = true;
                        return;
                    }

                    //check every 10ms
                    Thread.Sleep(10);
                }
            } while (Console.ReadKey(true).KeyChar.ToString() != m_quitKey);

            //the stop key was pressed, need to abort sweep
            m_stopSweep = true;
            return;
        }

    }
}
