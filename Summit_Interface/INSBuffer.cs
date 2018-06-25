using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Summit_Interface
{

    //Buffer class for thread-safe storage of data packets from the INS before sending off to Open-Ephys or saving to disk
    public class INSBuffer
    {
        private int m_nChans; //number of recording channels
        private int m_bufferSize; //buffer size

        private int m_currentBufferInd; //current position of the buffer
        private bool m_isFull; //if buffer is full or not
        private bool m_isEmpty; //if buffer is empty or not
        private double[,] m_bufferData; //actual data in the buffer
        private double[] m_CTMPacketNums; //vector holding the packet number of the data from the CTM packets (for detecting dropped packets)
        private double[] m_CTMTimestamps; //vector holding the timestamps of the data from the CTM packets (for detecting dropped packets)
        private double[] m_isDropped; //vector indicating whether the sample is from a dropped packet and interpolated or not
        private int[] m_stimClass; //vector indicating what stim protocol the decoder said to use (putting this in the this buffer right now for testing and saving purposes)
        private int m_nextStimClass; //because stim events are comining in async, one might come in when the buffer is empty, in which case I will just add it to the next timepoint that gets added to the buffer (and indicate the delay by adding 100)
        private ReaderWriterLockSlim RWLock; //lock for thread-safety

        //constructor
        public INSBuffer(int nChans, int bufferSize)
        {
            m_nChans = nChans;
            m_bufferSize = bufferSize;
            m_bufferData = new double[m_nChans, m_bufferSize];
            m_CTMPacketNums = new double[m_bufferSize];
            m_CTMTimestamps = new double[m_bufferSize];
            m_isDropped = new double[m_bufferSize];
            m_stimClass = new int[m_bufferSize];
            m_nextStimClass = 0;
            m_currentBufferInd = -1;
            m_isFull = false;
            m_isEmpty = true;
            RWLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        }

        //see if buffer is empty
        public bool isEmpty()
        {
            return m_isEmpty;
        }

        //see if buffer is full
        public bool isFull()
        {
            return m_isFull;
        }

        //num chan accessor
        public int getNumChans()
        {
            return m_nChans;
        }

        //buffer size accessor
        public int getBufferSize()
        {
            return m_bufferSize;
        }

        //Get how many samples are currently in the buffer
        public int getNumBufferSamples()
        {
            if (m_isEmpty)
            {
                return 0;
            }
            else if (m_isFull)
            {
                return m_bufferSize;
            }
            else
            {
                return m_currentBufferInd + 1;
            }
        }

        //set the write lock, make sure to unlock it with unlockWriter()!!
        public void lockWriter()
        {
            RWLock.EnterWriteLock();
        }

        //unlock the write lock
        public void unlockWriter()
        {
            RWLock.ExitWriteLock();
        }

        //function to flush buffer
        public void FlushBuffer()
        {
            RWLock.EnterWriteLock(); //Critical section start---------

            //don't actually delete data, just put index back to start
            m_currentBufferInd = -1;
            m_isEmpty = true;
            m_isFull = false;

            RWLock.ExitWriteLock(); //Critical section stop----------
        }


        //set the stim class at the current time point
        public void setStim(int stimClass)
        {
            RWLock.EnterWriteLock(); //Critical section start---------

            //if buffer is empty write now, just add it once the next packet comes in
            if (m_isEmpty)
            {
                m_nextStimClass = stimClass + 100;
            }
            else
            {
                m_stimClass[m_currentBufferInd] = stimClass;
            }

            RWLock.ExitWriteLock(); //Critical section stop----------
        }


        //add data into buffer
        public bool addData(double[,] data, double CTMNum, double timestamp, double isDroppedPacket, bool manualLock = false)
        {
            bool success = true;

            //check data has right size
            if (data.GetLength(0) != m_nChans)
            {
                success = false;
                return success;
            }

            //total number of samples to add
            int nSamples = data.GetLength(1);
            if (nSamples > m_bufferSize)
            {
                Console.WriteLine("Warning: Writing data to buffer which is larger than buffer size!");
            }

            if (!manualLock)
            {
                RWLock.EnterWriteLock(); //Critical section start---------
            }

            //loop through for all the samples we want to add
            for (int iSample = 0; iSample < nSamples; iSample++)
            {
                //increment index
                if (m_currentBufferInd == m_bufferSize - 1)
                {
                    m_currentBufferInd = 0;
                    m_isFull = true;
                }
                else
                {
                    m_currentBufferInd++;
                }
                m_isEmpty = false;

                //add data
                for (int iChan = 0; iChan < m_nChans; iChan++)
                {
                    m_bufferData[iChan, m_currentBufferInd] = data[iChan, iSample];
                }

                //add stim
                if (m_nextStimClass != 0) //0 indicates that there isn't a previous stim value to add
                {
                    m_stimClass[m_currentBufferInd] = m_nextStimClass;
                    m_nextStimClass = 0;
                }
                else
                {
                    m_stimClass[m_currentBufferInd] = -1; //-1 default value to indicate no stim result from Open-Ephys
                }

                //add packet number
                m_CTMPacketNums[m_currentBufferInd] = CTMNum;

                //add timestamp
                m_CTMTimestamps[m_currentBufferInd] = timestamp;

                //add dropped packet indicator
                m_isDropped[m_currentBufferInd] = isDroppedPacket;
            }

            if (!manualLock)
            {
                RWLock.ExitWriteLock(); //Critical section stop----------
            }

            return success;
        }


        //get all data currently in buffer as an array of doubles
        public double[,] getData(bool getStim, bool getPktNum, bool getTimestamp, bool getIsDropped, bool flush)
        {
            double[,] data;

            if (m_isEmpty)
            {
                //buffer is empty
                data = new double[0, 0];
                return data;
            }

            //size of array to return
            int nOutChans = m_nChans;
            if (getStim)
            {
                nOutChans++;
            }
            if (getPktNum)
            {
                nOutChans++;
            }
            if (getTimestamp)
            {
                nOutChans++;
            }
            if (getIsDropped)
            {
                nOutChans++;
            }

            int readInd;
            int outInd = 0;

            RWLock.EnterReadLock(); //Critical section start---------

            if (m_isFull) //if buffer is full, start ind is 1 point in front of current ind
            {
                if (m_currentBufferInd == m_bufferSize - 1) //unless the current ind is at the end
                {
                    readInd = 0; //in which case the start ind is still 0
                }
                else
                {
                    readInd = m_currentBufferInd + 1;
                }

                data = new double[nOutChans, m_bufferSize];
            }
            else //buffer not full, start ind is 0
            {
                readInd = 0;
                data = new double[nOutChans, m_currentBufferInd + 1];
            }

            //go through and put all buffer data into the output
            while (true)
            {

                //write data for each channel
                for (int iChan = 0; iChan < m_nChans; iChan++)
                {
                    data[iChan, outInd] = m_bufferData[iChan, readInd];
                }

                int iOutChan = m_nChans;

                //write stim
                if (getStim)
                {
                    data[iOutChan, outInd] = m_stimClass[readInd];
                    iOutChan++;
                }

                //write packet number
                if (getPktNum)
                {
                    data[iOutChan, outInd] = m_CTMPacketNums[readInd];
                    iOutChan++;
                }

                //write timestamp
                if (getTimestamp)
                {
                    data[iOutChan, outInd] = m_CTMTimestamps[readInd];
                    iOutChan++;
                }

                //write if is dropped packet
                if (getIsDropped)
                {
                    data[iOutChan, outInd] = m_isDropped[readInd];
                }

                //stop if we reach end of data
                if (readInd == m_currentBufferInd)
                {
                    break;
                }

                //increment to next time point in buffer
                if (readInd == m_bufferSize - 1)
                {
                    readInd = 0;
                }
                else
                {
                    readInd++;
                }
                outInd++;

            }

            RWLock.ExitReadLock(); //Critical section stop----------

            //flush the buffer
            if (flush)
            {
                FlushBuffer(); //has crtical section
            }

            return data;
        }


        //serialize buffer data into byte array
        //
        //Serialization is:
        //
        //  int32 number of buffer time points that are in this ZMQ packet
        //
        //  double data of channel 1 at time point 1,
        //  double data of channel 2 at time point 1,
        //  .
        //  .
        //  .
        //  double data of channel m_nChans, time point 1,
        //  double CTM packet number of time point 1,
        //
        //  double data of channel 1 at time point 2,
        //  double data of channel 2 at time point 2,
        //  .
        //  .
        //  .
        //  double data of channel m_nChans, time point 2,
        //  double CTM packet number of time point 2,
        //  
        //  .
        //  .
        //  .
        //  double data of channel m_nChans at time point m_currentBufferInd
        //  double CTM packet number of time point m_currentBufferInd,
        public byte[] getDataByteArray(bool flush)
        {
            byte[] byteArray;
            int writeInd;

            RWLock.EnterReadLock(); //Critical section start---------

            if (m_isFull) //if buffer is full, start ind is 1 point in front of current ind
            {
                if (m_currentBufferInd == m_bufferSize - 1) //unless the current ind is at the end
                {
                    writeInd = 0; //in which case the start ind is still 0
                }
                else
                {
                    writeInd = m_currentBufferInd + 1;
                }

                //first say how many time points are in the buffer
                byteArray = BitConverter.GetBytes(m_bufferSize);
            }
            else //buffer not full, start ind is 0
            {
                writeInd = 0;

                //first say how many time points are in the buffer
                byteArray = BitConverter.GetBytes(m_currentBufferInd + 1);
            }

            //check that the buffer has data
            if (m_isEmpty)
            {
                RWLock.ExitReadLock(); //Critical section stop----------
                return byteArray;
            }

            //go through and put all buffer data into the byte array
            while (true)
            {

                //write data for each channel
                for (int chan = 0; chan < m_nChans; chan++)
                {
                    byte[] data = BitConverter.GetBytes(m_bufferData[chan, writeInd]);
                    byteArray = Concatenate(byteArray, data);
                }

                //write packet number
                byte[] packNum = BitConverter.GetBytes(m_CTMPacketNums[writeInd]);
                byteArray = Concatenate(byteArray, packNum);

                //stop if we reach end of data
                if (writeInd == m_currentBufferInd)
                {
                    break;
                }

                //increment to next time point in buffer
                if (writeInd == m_bufferSize - 1)
                {
                    writeInd = 0;
                }
                else
                {
                    writeInd++;
                }
            }

            RWLock.ExitReadLock(); //Critical section stop----------

            //flush the buffer
            if (flush)
            {
                FlushBuffer(); //has crtical section
            }

            return byteArray;
        }


        //helper function for concatenating byte arrays
        public byte[] Concatenate(byte[] first, byte[] second)
        {
            byte[] ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }

    }

}
