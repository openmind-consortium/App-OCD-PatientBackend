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

#ifndef SUMMITSOURCE_H_INCLUDED
#define SUMMITSOURCE_H_INCLUDED

#ifdef _WIN32
#include <Windows.h>
#endif

#include <ProcessorHeaders.h>
#include "zmq.hpp"
#include <fstream>
#include <chrono>

/**

  This class serves as a template for creating new processors.

  If this were a real processor, this comment section would be used to
  describe the processor's function.

  @see GenericProcessor

*/

class SummitSource : public GenericProcessor

{
public:

    /** The class constructor, used to initialize any members. */
	SummitSource();

    /** The class destructor, used to deallocate memory */
	~SummitSource();

    /** Determines whether the processor is treated as a source. */
    bool isSource()
    {
        return true;
    }

    /** Determines whether the processor is treated as a sink. */
    bool isSink()
    {
        return false;
    }

	/** Indicates if the processor has a custom editor. Defaults to false */
	//bool hasEditor() const
	//{
	//	return true;
	//}

	/** If the processor has a custom editor, this method must be defined to instantiate it. */
	//AudioProcessorEditor* createEditor();

	/** Optional method that informs the GUI if the processor is ready to function. If false acquisition cannot start. Defaults to true */
	//bool isReady();

    /** Defines the functionality of the processor.

        The process method is called every time a new data buffer is available.

        Processors can either use this method to add new data, manipulate existing
        data, or send data to an external target (such as a display or other hardware).

        Continuous signals arrive in the "buffer" variable, event data (such as TTLs
        and spikes) is contained in the "events" variable.
         */
	void process(AudioSampleBuffer& buffer) override;

    /** The method that standard controls on the editor will call.
		It is recommended that any variables used by the "process" function 
		are modified only through this method while data acquisition is active. */
	void setParameter(int parameterIndex, float newValue) override;

	/** Optional method called every time the signal chain is refreshed or changed in any way.
		
		Allows the processor to handle variations in the channel configuration or any other parameter
		passed down the signal chain. The processor can also modify here the settings structure, which contains
		information regarding the input and output channels as well as other signal related parameters. Said
		structure shouldn't be manipulated outside of this method.
	*/
	//void updateSettings();

	int getNumOutputs() const override;
	float getSampleRate(int subProcessorIdx = 0) const override;
	bool enable() override;
	int getDefaultNumDataOutputs(DataChannel::DataChannelTypes type, int subProcessorIdx = 0) const override;

private:

    // private members and methods go here
    //
    // e.g.:
    //
    // float threshold;
    // bool state;

    JUCE_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(SummitSource);
	zmq::context_t context = zmq::context_t(1);
	zmq::socket_t socket = zmq::socket_t(context, ZMQ_REQ);
	std::ofstream debugFile;
	std::string debugPath = "SummitSource_debug.txt";
	void deserialize(float** data, int* packNums, int &length, zmq::message_t* reply);

	int nFeatureChans;
	int nChans;
	int INSBufferSize;
	int m_loop;
	int m_featuresHistory;
	int m_sampleCounter;
	int m_packetNumPrev;
	int m_packetDropSize;

	float** INSData;
	int* packetNumbers;

	std::ofstream m_profilingFile;
	long long m_elapsed; //in microseconds
	std::chrono::high_resolution_clock::time_point m_start_time;
	std::chrono::high_resolution_clock::time_point m_end_time;
};

#endif  // SUMMITSOURCE_H_INCLUDED
