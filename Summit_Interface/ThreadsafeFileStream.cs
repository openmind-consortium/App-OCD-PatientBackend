using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;


namespace Summit_Interface
{

    //Simple wrapper around System.IO.StreamWriter to make it thread safe (for logging thread timing info)
    class ThreadsafeFileStream
    {
        private System.IO.StreamWriter m_fileStream; // the actual file stream
        private ReaderWriterLockSlim m_RWLock; //lock for thread-safety

        //constructor
        public ThreadsafeFileStream(string filename)
        {
            m_fileStream = new System.IO.StreamWriter(filename);
            m_RWLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        }

        //close file
        public void closeFile()
        {
            m_fileStream.Close();
        }

        //write to file
        public void Write(string inputString)
        {
            m_RWLock.EnterWriteLock();
            m_fileStream.Write(inputString);
            m_RWLock.ExitWriteLock();
        }

        //write line to file
        public void WriteLine(string inputString)
        {
            m_RWLock.EnterWriteLock();
            m_fileStream.WriteLine(inputString);
            m_RWLock.ExitWriteLock();
        }
    }

}
