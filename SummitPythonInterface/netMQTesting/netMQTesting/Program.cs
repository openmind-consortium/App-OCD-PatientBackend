using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ZeroMQ;

namespace netMQTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            string ack = "Stim Updated";

            // Create
            using (var zMQContext = new ZContext())
            using (var stimSocket = new ZSocket(zMQContext, ZSocketType.REP))
            {
                // Bind
                stimSocket.Bind("tcp://*:12345");

                while (true)
                {
                    // Receive
                    using (ZFrame request = stimSocket.ReceiveFrame())
                    {
                        Console.WriteLine("Received {0}", request.ReadString());

                        // Do some work
                        Thread.Sleep(1);

                        // Send
                        stimSocket.Send(new ZFrame(ack));
                    }
                }
            }
        }
    }
}
