import time

# Import ZeroMQ package
import zmq
import json
import traceback

# Initialize the ZeroMQ context
context = zmq.Context()

# Configure ZeroMQ to send messages

zmqSend = context.socket(zmq.REQ)
# The communication is made on socket 12345
zmqSend.bind("tcp://eth0:12345")

def messageTrans(sock, paramDict):
    paramStr = json.dumps(paramDict)
    print("Sending %s" % paramStr)
    sock.send(paramStr.encode(encoding = 'UTF-8'))
    print("Sent transmission...")
    #  Get the reply.
    message = sock.recv()
    print("Received reply %s " % message)
    return message

stimParams = {
    'Group' : 0,
    'Frequency' : 100,
    'DurationInMilliseconds' : 250,
    'Amplitude' : [0,0,0,0],
    'PW' : [250,250,250,250],
    'ForceQuit' : False,
    'AddReverse' : True
    }

try:
    while(True):
        stimParams['Amplitude'][0] = 1
        message = messageTrans(zmqSend, stimParams)
        time.sleep(2)
        stimParams['Amplitude'][0] = 0
        stimParams['Amplitude'][1] = 1
        message = messageTrans(zmqSend, stimParams)
        time.sleep(2)
        stimParams['Frequency'] = 25
        stimParams['Amplitude'][0] = 1
        stimParams['Amplitude'][1] = 0
        message = messageTrans(zmqSend, stimParams)
        time.sleep(2)
        stimParams['Amplitude'][0] = 1
        stimParams['Amplitude'][1] = 1
        message = messageTrans(zmqSend, stimParams)
        time.sleep(2)
        stimParams['Amplitude'][0] = 0
        stimParams['Amplitude'][1] = 1
        stimParams['AddReverse'] = False
        message = messageTrans(zmqSend, stimParams)
        time.sleep(2)
        stimParams['Frequency'] = 100
        stimParams['AddReverse'] = True
        print('Sent one loop')
finally:
    stimParams['ForceQuit'] = True
    messageTrans(zmqSend, stimParams)
