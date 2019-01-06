import time

# Import ZeroMQ package
import zmq

# Initialize the ZeroMQ context
context = zmq.Context()

# Configure ZeroMQ to send messages

zmqSend = context.socket(zmq.PUB)
# The communication is made on socket 12345
zmqSend.bind("tcp://*:12345)

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
        zmqSend.send_json(stimParams)
        time.sleep(3)
        stimParams['Amplitude'][1] = 1
        zmqSend.send_json(stimParams)
        time.sleep(3)
        stimParams['Frequency'] = 25
        stimParams['Amplitude'][0] = 1
        zmqSend.send_json(stimParams)
        time.sleep(3)
        stimParams['Amplitude'][0] = 1
        stimParams['Amplitude'][1] = 1
        zmqSend.send_json(stimParams)
        time.sleep(3)
        stimParams['Amplitude'][0] = 1
        stimParams['AddReverse'] = False
        zmqSend.send_json(stimParams)
        time.sleep(3)
        stimParams['Frequency'] = 100
        stimParams['AddReverse'] = True
finally:
    stimParams['ForceQuit'] = True
    zmqSend.send_json(stimParams)
