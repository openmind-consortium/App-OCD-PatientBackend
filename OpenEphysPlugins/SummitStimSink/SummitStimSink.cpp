
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
#include "SummitStimSink.h"

#define PRINT_PROFILING

//If the processor uses a custom editor, it needs its header to instantiate it
//#include "TestSourceEditor.h"

SummitStimSink::SummitStimSink()
    : GenericProcessor("Summit Stim Sink") //, threshold(200.0), state(true)

{
	//Without a custom editor, generic parameter controls can be added
    //parameters.add(Parameter("thresh", 0.0, 500.0, 200.0, 0));

	//  Prepare our context and socket
	//context(1);
	//context = zmq_ctx_new();
	//socket(context, ZMQ_SUB);
	m_inputChan = 0;
	m_loop = 0;
	m_socket.connect("tcp://localhost:12345");

	m_debugFile.open(m_debugPath);
	m_debugFile << "Starting \n";

#ifdef PRINT_PROFILING
	m_profilingFile.open("SummitSink_Profiling.txt");
	m_profilingFile << "Loop GettingClass SendToSummit" << std::endl;
#endif

}


SummitStimSink::~SummitStimSink()
{
	m_debugFile.close();

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

void SummitStimSink::setParameter(int parameterIndex, float newValue)
{

    //Parameter& p =  parameters.getReference(parameterIndex);
    //p.setValue(newValue, 0);

    //threshold = newValue;

    //std::cout << float(p[0]) << std::endl;
    editor->updateParameterButtons(parameterIndex);
}

void SummitStimSink::process(AudioSampleBuffer& buffer)
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

	//Get decoded class from AUX channel
	m_start_time = std::chrono::high_resolution_clock::now();

	//EEG Test
	int iChan = m_AUXChannels[m_inputChan];
	int nSamples = getNumSamples(iChan);

	//if no samples, don't do anything
	if (nSamples == 0)
	{
		return;
	}
	
	const float* readPtr = buffer.getReadPointer(iChan);
	m_class = *readPtr;

	////EEG Test
	//if (m_class != 0)
	//{
	//	m_class = 1;
	//}

	m_end_time = std::chrono::high_resolution_clock::now();
#ifdef PRINT_PROFILING
	m_elapsed = std::chrono::duration_cast<std::chrono::microseconds>(m_end_time - m_start_time).count();
	m_profilingFile << std::to_string(m_elapsed) << " ";
#endif


	//Send to Summit system
	m_start_time = std::chrono::high_resolution_clock::now();

	zmq::message_t message(1);
	//assert(m_class == 0 || m_class == 1 || m_class == 2);

	if (m_loop < 11)
	{
		m_class = m_prevClass;
		m_loop++;
	}
	
	if (m_prevClass != m_class)
	{
		m_loop = 0;
		m_prevClass = m_class;
	}

	m_prevClass = m_class;


	memcpy(message.data(), std::to_string(m_class).c_str(), 1);
	m_socket.send(message);

	m_end_time = std::chrono::high_resolution_clock::now();
#ifdef PRINT_PROFILING
	m_elapsed = std::chrono::duration_cast<std::chrono::microseconds>(m_end_time - m_start_time).count();
	m_profilingFile << std::to_string(m_elapsed) << std::endl;
#endif

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

	m_loop++;
}

bool SummitStimSink::enable()
{
	//before start closed loop, set the input channels and output channel
	int nAUXInputs = 0;
	int nHEADInputs = 0;
	for (int iChan = 0; iChan < dataChannelArray.size(); iChan++)
	{
		if (dataChannelArray[iChan]->getChannelType() == DataChannel::AUX_CHANNEL)
		{
			m_AUXChannels.push_back(iChan);
			nAUXInputs++;
		}

		if (dataChannelArray[iChan]->getChannelType() == DataChannel::HEADSTAGE_CHANNEL)
		{
			m_HEADChannels.push_back(iChan);
			nHEADInputs++;
		}
	}

	m_nAUXInputs = nAUXInputs;
	m_nHEADInputs = nHEADInputs;
	
	return true;

	setAllChannelsToRecord();
}