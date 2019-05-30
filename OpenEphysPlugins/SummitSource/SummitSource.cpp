
/*
    ------------------------------------------------------------------

    This file is part of the Open Ephys GUI
    Copyright (C) 2014 Open Ephys

    ------------------------------------------------------------------

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/


#include <stdio.h>
#include "SummitSource.h"

#define PRINT_PROFILING

//If the processor uses a custom editor, it needs its header to instantiate it
//#include "TestSourceEditor.h"

SummitSource::SummitSource()
    : GenericProcessor("Summit Source") //, threshold(200.0), state(true)

{
	//Without a custom editor, generic parameter controls can be added
    //parameters.add(Parameter("thresh", 0.0, 500.0, 200.0, 0));

	settings.sampleRate = 30000;
	nFeatureChans = 15;

	socket.connect("tcp://localhost:5555");

	debugFile.open(debugPath);
	debugFile << "Starting \n";

#ifdef PRINT_PROFILING
	m_profilingFile.open("SummitSource_Profiling.txt");
	m_profilingFile << "Loop WaitingforReply Deserialization WritingToBuffer " << std::endl;
#endif

	m_loop = 0;
	m_featuresHistory = 15;
}


SummitSource::~SummitSource()
{
	delete[] chanMeans;
	debugFile.close();

	//deallocate memory
	for (int iChan = 0; iChan < nChans; iChan++)
	{
		delete[] INSData[iChan];
	}
	delete[] INSData;
	delete[] packetNumbers;

#ifdef PRINT_PROFILING
	m_profilingFile.close();
#endif

	//socket.close();
	//context.close();
}

/**
	If the processor uses a custom editor, this method must be present.
*/
/*
AudioProcessorEditor* TestSource::createEditor()
{
	editor = new TestSourceEditor(this, true);

	//std::cout << "Creating editor." << std::endl;

	return editor;
}
*/

void SummitSource::setParameter(int parameterIndex, float newValue)
{

    //Parameter& p =  parameters.getReference(parameterIndex);
    //p.setValue(newValue, 0);

    //threshold = newValue;

    //std::cout << float(p[0]) << std::endl;
    editor->updateParameterButtons(parameterIndex);
}

void SummitSource::process(AudioSampleBuffer& buffer,
                               MidiBuffer& events)
{
	/**
	Generic structure for processing buffer data 
	*/

	//
	//zmq::message_t request;
	//  Wait for next request from client
	//socket.recv(&request);
	//std::string mesData(reinterpret_cast<char*>(request.data()),request.size());

	//int nChannels = buffer.getNumChannels();
	//float* samplePtr = buffer.getWritePointer(0, 0);
	//*samplePtr = atof(mesData.c_str());

	////debugFile << std::to_string(*samplePtr) << " ";
	//debugFile << mesData;
	//debugFile << "\n";

#ifdef PRINT_PROFILING
	m_profilingFile << std::to_string(m_loop) << " ";
#endif


	//ask for data with ZMQ
	m_start_time = std::chrono::high_resolution_clock::now();

	zmq::message_t request(2);
	memcpy(request.data(), "TD", 2);
	socket.send(request);

	zmq::message_t reply;
	socket.recv(&reply);

	m_end_time = std::chrono::high_resolution_clock::now();
#ifdef PRINT_PROFILING
	m_elapsed = std::chrono::duration_cast<std::chrono::microseconds>(m_end_time - m_start_time).count();
	m_profilingFile << std::to_string(m_elapsed) << " ";
#endif


	//deserialize data from ZMQ socket to data arrays
	m_start_time = std::chrono::high_resolution_clock::now();

	int packetLength;
	deserialize(INSData, packetNumbers, packetLength, &reply);
	if (m_packetNumPrev - packetNumbers[0] > 1)
	{
		m_sampleCounter += m_packetDropSize*(m_packetNumPrev - packetNumbers[0] - 1);
	}
	else
	{
		m_sampleCounter += packetLength;
	}

	m_end_time = std::chrono::high_resolution_clock::now();
#ifdef PRINT_PROFILING
	m_elapsed = std::chrono::duration_cast<std::chrono::microseconds>(m_end_time - m_start_time).count();
	m_profilingFile << std::to_string(m_elapsed) << " ";
#endif


	int nChannels = buffer.getNumChannels();

	//Temporarily for now, subtract the values of all the channels at the begining to center the data to zero.
	if (m_loop == 0)
	{
		for (int iChan = 0; iChan < nChans; iChan++)
		{
			chanMeans[iChan] = 0;
			for (int iSample = 0; iSample < packetLength; iSample++)
			{
				chanMeans[iChan] += INSData[iChan][iSample];
			}
			chanMeans[iChan] /= packetLength;
		}
	}

	//// write samples to file
	//if (packetLength == 0)
	//{
	//	debugFile << std::to_string(0) << " ";
	//	for (int iChan = 0; iChan < nChans; iChan++)
	//	{
	//		debugFile << "NaN NaN ";
	//	}
	//	debugFile << std::endl;
	//}
	//for (int iSample = 0; iSample < packetLength; iSample++)
	//{
	//	debugFile << std::to_string(packetLength) << " ";
	//	debugFile << packetNumbers[iSample] << " ";
	//	for (int iChan = 0; iChan < nChans; iChan++)
	//	{
	//		debugFile << std::to_string(INSData[iChan][iSample]) << " ";
	//		debugFile << std::to_string(INSData[iChan][iSample]- chanMeans[iChan]) << " ";
	//	}
	//	debugFile << std::endl;

	//}

	//now fill the channels (raw data for headstage channels, features for aux channels
	m_start_time = std::chrono::high_resolution_clock::now();
	
	int iHeadstage = 0;

	int iChanHist = 0;
	int iHist = 0;
	bool giveZeroes = false;

	if (packetLength != 0)
	{
		debugFile << std::to_string(packetNumbers[0]) << " ";
		debugFile << std::to_string(m_sampleCounter) << " ";
	}

	for (int iChan = 0; iChan < channels.size(); iChan++)
	{
		float* samplePtr = buffer.getWritePointer(iChan, 0);

		switch (channels[iChan]->getType())
		{
		
		case HEADSTAGE_CHANNEL:
		{
			//saved all our data channels already
			if (iHeadstage > nChans-1)
			{
				break;
			}

			//add next data channel to headstage output channel
			for (int iSample = 0; iSample < packetLength; iSample++)
			{
				*(samplePtr + iSample) = INSData[iChan][iSample] * 1000 -chanMeans[iHeadstage];
			}
			iHeadstage++;

			break;
		}

		case AUX_CHANNEL:
		{
			//Theres no more data to use for history, just send what we have
			if (iHist > packetLength-1)
			{
				for (int iSample = 0; iSample < packetLength; iSample++)
				{
					*(samplePtr + iSample) = INSData[iChanHist][packetLength - 1] * 1000;// -chanMeans[iChanToUse]);
				}
				break;
			}

			//We added all the history for one channel, start adding history for next channel
			if (iHist > m_featuresHistory)
			{
				iHist = 0;
				iChanHist++;
			}

			//We added all the history for all the channels, we're done
			if (iChanHist > 0)
			{
				break;
			}

			//Add history
			for (int iSample = 0; iSample < packetLength; iSample++)
			{
				*(samplePtr + iSample) = INSData[iChanHist][packetLength - 1 - iHist] * 1000;// -chanMeans[iChanToUse]);
			}

			debugFile << std::to_string(INSData[iChanHist][packetLength - 1 - iHist] * 1000) << " ";

			iHist++;

			break;
		}

		}
	}

	if (packetLength != 0)
	{
		debugFile << std::endl;
	}

	m_end_time = std::chrono::high_resolution_clock::now();
#ifdef PRINT_PROFILING
	m_elapsed = std::chrono::duration_cast<std::chrono::microseconds>(m_end_time - m_start_time).count();
	m_profilingFile << std::to_string(m_elapsed) << std::endl;
#endif

	setNumSamples(events, packetLength);
	
	m_loop++;
}

bool SummitSource::enable()
{
	//connect to Summit API
	zmq::message_t request(6);
	memcpy(request.data(), "InitTD", 6);
	socket.send(request);

	zmq::message_t reply;
	socket.recv(&reply);

	int* dataBytes = new int[2];
	memcpy(dataBytes, reply.data(), 8);

	nChans = dataBytes[0];
	INSBufferSize = dataBytes[1];
	chanMeans = new float[nChans];

	
	//allocate memory
	INSData = new float*[nChans];
	for (int iChan = 0; iChan < nChans; iChan++)
	{
		INSData[iChan] = new float[INSBufferSize];
	}

	packetNumbers = new int[INSBufferSize];

	m_sampleCounter = 0;
	
	delete [] dataBytes;
	return true;

	setAllChannelsToRecord();
}

//get ZMQ message as data
void SummitSource::deserialize(float** data, int* packNums, int &length, zmq::message_t* reply)
{
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
	//  double data of channel m_nChans at time point 2,
	//  double CTM packet number of time point 2,
	//  
	//  .
	//  .
	//  .
	//  double data of channel m_nChans at time point m_currentBufferInd
	//  double CTM packet number of time point m_currentBufferInd,

	//get the length (as int) of the incoming data (first 4 bytes)
	int* intData = static_cast<int*>(reply->data());
	memcpy(&length, intData, 4);
	intData++;

	//rest of the serialization is as doubles
	double* doubleData = reinterpret_cast<double*>(intData);

	//initialize memory	
	double** doubleArray = new double*[nChans];
	for (int iChans = 0; iChans < nChans; iChans++)
	{
		doubleArray[iChans] = new double[length];
	}
	double packet;

	//copy data
	for (int iPoint = 0; iPoint < length; iPoint++)
	{
		//channel data
		for (int iChans = 0; iChans < nChans; iChans++)
		{
			memcpy(&doubleArray[iChans][iPoint], doubleData, 8);
			data[iChans][iPoint] = (float)doubleArray[iChans][iPoint];
			doubleData++;
		}

		//INS packet number
		memcpy(&packet, doubleData, 8);
		packNums[iPoint] = (int)packet;
		doubleData++;
	}

	//free memory
	for (int iChans = 0; iChans < nChans; iChans++)
	{
		delete[] doubleArray[iChans];
	}
	delete[] doubleArray;
}

float SummitSource::getSampleRate()
{
	return 500.0f;
}

int SummitSource::getNumHeadstageOutputs()
{
	return 4;
}

int SummitSource::getNumAuxOutputs()
{
	return 20;
}