using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NetMQ;
using NetMQ.Sockets;

namespace netMQTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            string ack = "Stim Updated";

            // Create
            using (ResponseSocket stimSocket = new ResponseSocket())
            {
                // Bind
                stimSocket.Connect("tcp://192.168.4.2:12345");

                while (true)
                {
                    // Receive
                    Console.WriteLine("Waiting for Message...");
                    string gotMessage;
                    stimSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(1000), out gotMessage);
                    if (gotMessage == null) //no actual message received, just the timeout being hit
                    {
                        Console.WriteLine(" Waiting for a message...");
                        continue;
                    }
                    Console.WriteLine("Received {0}", gotMessage);

                    // Do some work
                    Thread.Sleep(1);

                    // Send
                    stimSocket.SendFrame(ack);
                    
                }
            }
        }
    }
}
