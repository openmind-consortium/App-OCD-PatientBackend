using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Collections;

using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Summit_Interface
{

    //class that holds the parameters of an INS session, loaded from a JSON file
    public class INSParameters
    {
        //struct holding information about each key-value pair in the JSON structure
        struct parameterField
        {
            public readonly string fieldName;
            public readonly Type valueType;
            public readonly string parent;
            public readonly string grandParent;
            public readonly bool hasChildren;
            public readonly bool isArray;
            public readonly string specificValues;
            public readonly double[] valueLimits;
            public readonly string arraySizeDependency;
            public readonly int? manualArraySize;

            public parameterField(string fieldName_c, Type valueType_c, string parent_c, string grandParent_c, bool hasChildren_c,
                bool isArray_c, string specificValues_c, double[] valueLimits_c, string arraySizeDependency_c, int? manualArraySize_c)
            {
                this.fieldName = fieldName_c;
                this.valueType = valueType_c;
                this.parent = parent_c;
                this.grandParent = grandParent_c;
                this.hasChildren = hasChildren_c;
                this.isArray = isArray_c;
                this.specificValues = specificValues_c;
                this.valueLimits = valueLimits_c;
                this.arraySizeDependency = arraySizeDependency_c;
                this.manualArraySize = manualArraySize_c;
            }
        };

        private string m_fileName; //full path to json file containing the paraters for an experiment
        private parameterField[] m_allFields; //names and properties of all the fields in the parameter file definition
        private SortedList<string, object> m_fieldSpecificValues; //a list of specific values that certerain fields are only allowed to have (i.e. for enums)
        public readonly JObject m_parameters; //variable contianing the parsed json values

        //constructor
        public INSParameters(string fileName)
        {
            using (StreamReader reader = File.OpenText(@fileName))
            {
                m_parameters = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
            }

            //list of all the field names (v0.3)
            // v0.1 initial def
            // v0.2 added stim config button to read all group and program pairs
            // v0.3 added option to hide console and option for software testing only (no device so won't try connecting)

            //                      Field Name                          Value type          Parent                      grandParent  has Children?  Array?  sepcific values         [lowerbound upperbound] relative array size         absolute array size
            m_allFields = new parameterField[]{
                new parameterField("Version",                           typeof(string),     null,                       null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("StreamToOpenEphys",                 typeof(bool),       null,                       null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("NotifyCTMPacketsReceived",          typeof(bool),       null,                       null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("NotifyOpenEphysPacketsReceived",    typeof(bool),       null,                       null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("QuitButton",                        typeof(string),     null,                       null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("HideConsole",                       typeof(bool),       null,                       null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("NoDeviceTesting",                   typeof(bool),       null,                       null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("TelemetryMode",                     typeof(long),       null,                       null,               false,  false,  null,                   new double[2]{3, 4},    null,                       null),
                new parameterField("DisableAllCTMBeeps",                typeof(bool),       null,                       null,               false,  false,  null,                   null,                   null,                       null),

                new parameterField("Sense",                             null,               null,                       null,               true,   false,  null,                   null,                   null,                       null),
                new parameterField("APITimeSync",                       typeof(bool),       "Sense",                    null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("SaveFileName",                      typeof(string),     "Sense",                    null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("BufferSize",                        typeof(long),       "Sense",                    null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("ZMQPort",                           typeof(long),       "Sense",                    null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("InterpolateMissingPackets",         typeof(bool),       "Sense",                    null,               false,  false,  null,                   null,                   null,                       null),
                new parameterField("SamplingRate",                      typeof(long),       "Sense",                    null,               false,  false,  "samplingRates",        null,                   null,                       null),
                new parameterField("PacketPeriod",                      typeof(long),       "Sense",                    null,               false,  false,  "packetPeriods",        null,                   null,                       null),
                new parameterField("nChans",                            typeof(long),       "Sense",                    null,               false,  false,  null,                   new double[2]{1, 4},    null,                       null),
                new parameterField("Anode",                             typeof(long),       "Sense",                    null,               false,  true,   "electrodeNumbers",     null,                   "Sense.nChans",             null),
                new parameterField("Cathode",                           typeof(long),       "Sense",                    null,               false,  true,   "electrodeNumbers",     null,                   "Sense.nChans",             null),
                new parameterField("LowPassCutoffStage1",               typeof(long),       "Sense",                    null,               false,  true,   "stageOneLowPasses",    null,                   "Sense.nChans",             null),
                new parameterField("LowPassCutoffStage2",               typeof(long),       "Sense",                    null,               false,  true,   "stageTwoLowPasses",    null,                   "Sense.nChans",             null),
                new parameterField("HighPassCutoff",                    typeof(double),     "Sense",                    null,               false,  true,   "HighPasses",           null,                   "Sense.nChans",             null),
                
                new parameterField("FFT",                               null,               "Sense",                    null,               true,   false,  null,                   null,                   null,                       null),
                new parameterField("Enabled",                           typeof(bool),       "FFT",                      "Sense",            false,  false,  null,                   null,                   null,                       null),
                new parameterField("Channel",                           typeof(long),       "FFT",                      "Sense",            false,  false,  "senseChannels",        null,                   null,                       null),
                new parameterField("FFTSize",                           typeof(long),       "FFT",                      "Sense",            false,  false,  "FFTSizes",             null,                   null,                       null),
                new parameterField("FFTInterval",                       typeof(long),       "FFT",                      "Sense",            false,  false,  null,                   null,                   null,                       null),
                new parameterField("WindowEnabled",                     typeof(bool),       "FFT",                      "Sense",            false,  false,  null,                   null,                   null,                       null),
                new parameterField("WindowLoad",                        typeof(long),       "FFT",                      "Sense",            false,  false,  "windowLoads",          null,                   null,                       null),
                new parameterField("StreamSizeBins",                    typeof(long),       "FFT",                      "Sense",            false,  false,  null,                   null,                   null,                       null),
                new parameterField("StreamOffsetBins",                  typeof(long),       "FFT",                      "Sense",            false,  false,  null,                   null,                   null,                       null),

                new parameterField("BandPower",                         null,               "Sense",                    null,               true,   false,  null,                   null,                   null,                       null),
                new parameterField("FirstBandEnabled",                  typeof(bool),       "BandPower",                "Sense",            false,  true,   null,                   null,                   "Sense.nChans",             null),
                new parameterField("SecondBandEnabled",                 typeof(bool),       "BandPower",                "Sense",            false,  true,   null,                   null,                   "Sense.nChans",             null),
                new parameterField("FirstBandLower",                    typeof(long),       "BandPower",                "Sense",            false,  true,   null,                   null,                   "Sense.nChans",             null),
                new parameterField("FirstBandUpper",                    typeof(long),       "BandPower",                "Sense",            false,  true,   null,                   null,                   "Sense.nChans",             null),
                new parameterField("SecondBandLower",                   typeof(long),       "BandPower",                "Sense",            false,  true,   null,                   null,                   "Sense.nChans",             null),
                new parameterField("SecondBandUpper",                   typeof(long),       "BandPower",                "Sense",            false,  true,   null,                   null,                   "Sense.nChans",             null),


            };

            //list all the specific values that certain fields must take
            m_fieldSpecificValues = new SortedList<string, object>();
            m_fieldSpecificValues.Add("samplingRates", new specificValuesGeneric<long>(new List<long>() {       250, 500, 1000 }));
            m_fieldSpecificValues.Add("packetPeriods", new specificValuesGeneric<long>(new List<long>() {       30, 40, 50, 60, 70, 80, 90, 100 }));
            m_fieldSpecificValues.Add("electrodeNumbers", new specificValuesGeneric<long>(new List<long>() {    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }));
            m_fieldSpecificValues.Add("stageOneLowPasses", new specificValuesGeneric<long>(new List<long>() {   50, 100, 450 }));
            m_fieldSpecificValues.Add("stageTwoLowPasses", new specificValuesGeneric<long>(new List<long>() {   100, 160, 350, 1700 }));
            m_fieldSpecificValues.Add("HighPasses", new specificValuesGeneric<double>(new List<double>(){       0.85, 1.2, 3.3, 8.6 }));
            m_fieldSpecificValues.Add("senseChannels", new specificValuesGeneric<long>(new List<long>() {       0, 1, 2, 3 }));
            m_fieldSpecificValues.Add("FFTSizes", new specificValuesGeneric<long>(new List<long>() {            64, 256, 1024 }));
            m_fieldSpecificValues.Add("windowLoads", new specificValuesGeneric<long>(new List<long>() {         25, 50, 100 }));
            m_fieldSpecificValues.Add("rampingTypes", new specificValuesGeneric<string>(new List<string>(){     "None", "UpEnabled", "DownEnabled", "RepeatRampUp" }));

            //now do checking of all the loaded JSON fields to make sure everything is conforming to the JSON structure definition defined by m_allFields and m_fieldSpecificValues
            validateParameters();

            m_fileName = fileName;
        }

        //Method for making sure the loaded parameters are complete and correct
        private void validateParameters()
        {
            List<string> missingFields = new List<string>();
            List<List<string>> wrongTypes = new List<List<string>>(),
                wrongValues = new List<List<string>>(),
                outOfRange = new List<List<string>>(),
                wrongArrayness = new List<List<string>>(),
                wrongSize = new List<List<string>>();
            
            //do first check for non-reference dependent tests
            checkFieldsRecursive(m_parameters, null, ref missingFields, ref wrongTypes, ref wrongValues, ref outOfRange, ref wrongArrayness, ref wrongSize, false);
            //do second run for reference dependent tests
            checkFieldsRecursive(m_parameters, null, ref missingFields, ref wrongTypes, ref wrongValues, ref outOfRange, ref wrongArrayness, ref wrongSize, true);

            string errorMessage = "";

            //first add any missing fields
            if (missingFields.Any())
            {
                errorMessage += "\n Missing fields - \n";
                foreach (string field in missingFields)
                {
                    errorMessage += field + "\n";
                }
            }
            
            //next add any incorrect types
            if (wrongTypes.Any())
            {
                errorMessage += "\n Fields with incorrect type - \n";
                foreach (List<string> field in wrongTypes)
                {
                    errorMessage += "Field: " + field[0] + " requires type: " + field[1] + ", but has type: " + field[2] + "\n";
                }
            }

            //next add errors for specific values
            if (wrongValues.Any())
            {
                errorMessage += "\n Fields with incorrect values - \n";
                foreach (List<string> field in wrongValues)
                {
                    errorMessage += "Field: " + field[0] + " has value: " + field[1] + ", must be one of the following values: " + field[2] + "\n";
                }
            }

            //add errors for out of bound values
            if (outOfRange.Any())
            {
                errorMessage += "\n Fields with values out of bounds - \n";
                foreach (List<string> field in outOfRange)
                {
                    errorMessage += "Field: " + field[0] + " has value: " + field[1] + ", must be in range: " + field[2] + "\n";
                }
            }

            //add errors for arrayness
            if (wrongArrayness.Any())
            {
                errorMessage += "\n Fields with wrong \"arrayness\" - \n";
                foreach (List<string> field in wrongArrayness)
                {
                    errorMessage += "Field: " + field[0] + " is supposed to be an array? " + field[1] + ", but is it actually an array? " + field[2] + "\n";
                }
            }

            //add errors for array sizes
            if (wrongSize.Any())
            {
                errorMessage += "\n Fields with wrong array sizes - \n";
                foreach (List<string> field in wrongSize)
                {
                    errorMessage += "Field: " + field[0] + " has size " + field[1] + ", but is supposed to have size " + field[2] + field[3] + "\n";
                }
            }


            if (!String.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Error in JSON fields! \n" + errorMessage;
                throw new Exception(errorMessage);
            }

        }


        //helper function which determines if fields are missing
        private void checkFieldsRecursive(JObject token, string parentName, ref List<string> missingFields, ref List<List<string>> wrongTypes,
            ref List<List<string>> wrongValues, ref List<List<string>> outOfRange, ref List<List<string>> wrongArrayness, ref List<List<string>> wrongSize, bool secondPassCheck)
        {
            //first get all names of the fields (keys) of the JSON object (if it's not null)
            List<string> tokenKeys = new List<string>();
            if (token != null)
            {
                foreach (JProperty field in token.Children<JProperty>())
                {
                    tokenKeys.Add(field.Name);
                }
            }

            //get list of all children fields (parent == parentName)
            List<parameterField> childrenFields = (from field in m_allFields
                                                   where field.parent == parentName
                                                   select field).ToList();

            //go through the list that we just got, check if each item is in tokenKeys
            foreach (parameterField childField in childrenFields)
            {
                string fullPath = "";
                fullPath = getFullPath(childField, fullPath, true);
                bool isArray;

                if (!tokenKeys.Contains(childField.fieldName))
                {
                    if (!secondPassCheck)
                    {
                        //if an item is not there, get full path of item and add it to missing fields
                        missingFields.Add(fullPath);
                    }
                }

                else
                {

                    //check that if the field is supposed to be an array, it actually is
                    bool badType = false;
                    if (token[childField.fieldName].ToObject<object>().GetType() != typeof(JArray))
                    {
                        isArray = false;
                    }
                    else
                    {
                        isArray = true;
                    }

                    if ((childField.isArray && isArray == false) || (!childField.isArray && isArray == true))
                    {
                        //arrayness doesn't match
                        wrongArrayness.Add(new List<string>() { fullPath, childField.isArray.ToString(), isArray.ToString() });
                        badType = true;
                    }


                    //we know the field exists and the arrayness is correct, check if the type is correct (use list in case it's an array with multiple things inside)
                    if (!secondPassCheck && !badType)
                    {
                        List<Type> childTypes = new List<Type>() { token[childField.fieldName].ToObject<object>().GetType() };

                        //if it's an array, make sure it's not empty, and then get type of the elements in the array
                        if (isArray)
                        {
                            if (token[childField.fieldName].Count() != 0)
                            {
                                JToken[] childArray = token[childField.fieldName].ToArray();
                                childTypes.Clear();
                                foreach (JToken element in childArray)
                                {
                                    childTypes.Add(element.ToObject<object>().GetType());
                                }
                            }
                            else
                            {
                                //if it's empty, don't need to check type (we already checked arrayness)
                                childTypes[0] = null;
                            }
                        }

                        //now for the child token (or child tokens if it's an array), check the type, if not correct, put in wrongTypes
                        foreach (Type childType in childTypes)
                        {
                            if (childField.valueType != null && childType != null && childType != childField.valueType)
                            {
                                //the one exception is if it's supposed to be a double, but it's actually an int (since JSON.Net default reads numbers as ints)
                                if (childType == typeof(long) && childField.valueType == typeof(double))
                                {
                                    continue;
                                }

                                //otherwise, the type is wrong
                                wrongTypes.Add(new List<string> { fullPath, childField.valueType.ToString(), childType.ToString() });
                                badType = true;
                            }
                        }
                    }

                    //now check if it can only take specific values, and if the given value is valid (if the type isn't valid, don't bother checking)
                    if (childField.specificValues != null && !badType && !secondPassCheck)
                    {
                        //first make sure the key is valid with a try-catch
                        dynamic validValues;
                        try
                        {
                            validValues = m_fieldSpecificValues[childField.specificValues];
                        }
                        catch
                        {
                            throw new Exception(String.Format("The name of the specific values key: {0} in {1} isn't in the dictionary! Tell David to check JSON definitions",
                                childField.specificValues, fullPath));
                        }

                        //Next, make sure the class of the valid values are the same of the field
                        Type validValuesType = validValues.getValueType();
                        if (validValuesType != childField.valueType)
                        {
                            throw new Exception(String.Format("The type of the field {0} is set to be {1}, but the Specific Values for that field are {2}! Tell David to check JSON definitions",
                                 fullPath, childField.valueType, m_fieldSpecificValues[childField.specificValues].GetType()));
                        }

                        //finally do test of whether the value(s) is valid or not
                        validValues.checkValidValues(token[childField.fieldName], fullPath, ref wrongValues);

                    }


                    //check bounds, if any (don't need to do check if the type is bad though
                    if (childField.valueLimits != null & !badType & !secondPassCheck)
                    {
                        //make sure the types match, and are ints or doubles
                        if (childField.valueType != typeof(long) && childField.valueType != typeof(double))
                        {
                            throw new Exception(String.Format("To have value bounds the type must be int or double, but the values in {0} are of type {1}! Tell David to check JSON definitions",
                                fullPath, childField.valueType));
                        }

                        //cast all to double to save some lines of code
                        List<Double> childValues = new List<double>();
                        if (isArray)
                        {
                            //if its an array, then do check for each of the values
                            if (token[childField.fieldName].Count() != 0) //only do check if array isn't empty
                            {
                                JToken[] childArray = token[childField.fieldName].ToArray();
                                foreach (JToken element in childArray)
                                {
                                    childValues.Add(element.ToObject<double>());
                                }
                            }
                        }
                        else
                        {
                            childValues.Add((double)token[childField.fieldName]);
                        }

                        //do check
                        foreach (double childValue in childValues)
                        {
                            if (childValue < (double)childField.valueLimits[0] || childValue > (double)childField.valueLimits[1])
                            {
                                //add to list of out-of-bounds
                                outOfRange.Add(new List<string>() { fullPath, childValue.ToString(), "[" + childField.valueLimits[0].ToString()
                                + ", " + childField.valueLimits[1].ToString() + "]" });
                            }
                        }

                    }


                    //check if the array size is correct (done on second pass since need to make sure the dependency fields exist first)
                    //first check manual array sizes:
                    if (childField.manualArraySize != null && isArray && !secondPassCheck)
                    {
                        //just to make sure the arrayness is true
                        if (!childField.isArray)
                        {
                            throw new Exception(String.Format("Field {0} has a manual array size, but it's arrayness is false! Tell David to check JSON definitions",
                                fullPath));
                        }

                        int arraySize = token[childField.fieldName].ToArray().Length;
                        
                        if (arraySize!=childField.manualArraySize)
                        {
                            wrongSize.Add(new List<string>() { fullPath, arraySize.ToString(), childField.manualArraySize.ToString(), null });
                        }
                    }
                    //next check referenced array size
                    if (childField.arraySizeDependency != null && isArray && secondPassCheck)
                    {
                        //just to make sure the arrayness is true
                        if (!childField.isArray)
                        {
                            throw new Exception(String.Format("Field {0} has a manual array size, but it's arrayness is false! Tell David to check JSON definitions",
                                fullPath));
                        }

                        //don't need to do check if the dependency field is missing or the wrong type
                        List<string> wrongTypePaths = (from error in wrongTypes
                                                       select error[0]).ToList<string>();
                        if (!missingFields.Contains(childField.arraySizeDependency) && !wrongTypePaths.Contains(childField.arraySizeDependency))
                        {
                            //get the dependency
                            JToken dependencyToken;
                            try
                            {
                                dependencyToken = m_parameters.SelectToken(childField.arraySizeDependency);
                            }
                            catch
                            {
                                //the token couldn't be found
                                throw new Exception(String.Format("The array size dependancy field, {0}, for the field {1} couldn't be found! Tell David to check JSON definitions",
                                   childField.arraySizeDependency, fullPath));
                            }

                            //try casting to int
                            int dependencyValue;
                            try
                            {
                                dependencyValue = (int)dependencyToken;
                            }
                            catch
                            {
                                //the token can't be cast to int
                                throw new Exception(String.Format("The array size dependancy field, {0}, for the field {1} couldn't be cast to an int! Tell David to check JSON definitions",
                                   childField.arraySizeDependency, fullPath));
                            }

                            int arraySize = token[childField.fieldName].ToArray().Length;
                            //finally, see if the size matches
                            if (arraySize!=dependencyValue)
                            {
                                wrongSize.Add(new List<string> { fullPath, arraySize.ToString(), dependencyValue.ToString(), "(" + childField.arraySizeDependency + ")" });
                            }

                        }

                    }

                    //finally, if it has children, make recursive call to all child tokens
                    if (childField.hasChildren)
                    {
                        JObject child;

                        try
                        {
                            child = (JObject)token[childField.fieldName];
                        }
                        catch
                        {
                            //uh oh, this field isn't even an object (doesn't have children), put all children fields of this child field in missingFields
                            checkFieldsRecursive(null, childField.fieldName, ref missingFields, ref wrongTypes, ref wrongValues, 
                                ref outOfRange, ref wrongArrayness, ref wrongSize, secondPassCheck);
                            return;
                        }

                        checkFieldsRecursive(child, childField.fieldName, ref missingFields, ref wrongTypes, ref wrongValues, 
                            ref outOfRange, ref wrongArrayness, ref wrongSize, secondPassCheck);
                    }
                }

            }

        }



        string getFullPath(parameterField currentField, string path, bool isStart)
        {
            //recursive function to get the full path of a certain field starting from the JSON root

            //add current name to string
            path = (isStart ? currentField.fieldName : currentField.fieldName + ".") + path;

            //go to parent (if not root)
            if (currentField.parent != null)
            {
                //get parent
                parameterField parent = (from field in m_allFields
                                         where (field.fieldName == currentField.parent && field.parent == currentField.grandParent)
                                         select field).SingleOrDefault();

                //check to make sure that the parent actually exist (in case of typos when adding the parameterFields)
                if (parent.fieldName == null)
                {
                    throw new Exception(String.Format("The parent string, {0}, of the {0} field doesn't exist! Tell David to check JSON definitions",
                        currentField.parent, currentField.fieldName));
                }
                else
                {
                    //make recursive call
                    return getFullPath(parent, path, false);
                }
            }
            else //reached the top most level
            {
                return path;
            }

        }


        /// <summary>
        /// Function to check if the specified parameter field is an array or not
        /// </summary>
        /// <param name="pathToParam"></param>
        /// <returns></returns>
        public bool ParamIsArray(string pathToParam)
        {
            JToken selectedToken;

            //first try to get the token
            try
            {
                selectedToken = m_parameters.SelectToken(pathToParam);
                if (selectedToken == null)
                {
                    throw new Exception();
                }
            }
            catch
            {
                throw new Exception(String.Format("Field {0} doesn't exist! Tell David to check the JSON definitions!", pathToParam));
            }

            //return if it's an array or not
            return selectedToken.ToObject<object>().GetType() == typeof(JArray);
        }


        public dynamic GetParam(string pathToParam, Type paramType, int? paramIndex=null)
        {
            JToken selectedToken;

            //first try to get the token
            try
            {
                selectedToken = m_parameters.SelectToken(pathToParam);
                if (selectedToken == null)
                {
                    throw new Exception();
                }
            }
            catch
            {
                throw new Exception(String.Format("Field {0} doesn't exist! Tell David to check the JSON definitions!", pathToParam));
            }

            if (selectedToken.ToObject<object>().GetType() != typeof(JArray))
            {
                //if its not an array, cast and return
                try
                {
                    return Convert.ChangeType(selectedToken, paramType);

                }
                catch
                {
                    throw new Exception(String.Format("Cannot cast to {0} when getting {1}!", paramType.ToString(), pathToParam));
                }

                if (paramIndex != null)
                {
                    throw new Exception(String.Format("{0} is not an array, yet an array element was requested!", pathToParam));
                }
            }
            else
            {
                //if it is an array, return as a list
                List<dynamic> output = new List<dynamic>();
                try
                {
                    //if it is an array, return as a list
                    JToken[] theArray = selectedToken.ToArray();
                    foreach (JToken token in theArray)
                    {
                        output.Add(Convert.ChangeType(token, paramType));
                    }
                }
                catch
                {
                    throw new Exception(String.Format("Cannot cast to {0} when getting {1}!", paramType.ToString(), pathToParam));
                }

                if (paramIndex == null)
                {
                    //no index was specified, return the the whole array
                    return output;
                }
                else
                {
                    //return the element at the specified index
                    return output[paramIndex.Value];
                }
            }

        }


        private class specificValuesGeneric<T>
        {
            private List<T> m_validValues; //actual specific valid values are stored here

            public specificValuesGeneric()
            {
                m_validValues = new List<T>();

            }

            public specificValuesGeneric(List<T> values)
            {
                m_validValues = values;

            }

            public Type getValueType()
            {
                return typeof(T);
            }

            public void checkValidValues(JToken token, string tokenFullPath, ref List<List<string>> wrongValues)
            {

                //get the value of the JSON field (or all values if it's an array)
                List<T> values = new List<T>();
                try
                {
                    if (token.ToObject<object>().GetType() == typeof(JArray))
                    {
                        JToken[] theArray = token.ToArray();
                        foreach (JToken element in theArray)
                        {
                            values.Add((T)Convert.ChangeType(element, typeof(T)));
                        }
                    }
                    else
                    {
                        values.Add((T)Convert.ChangeType(token, typeof(T)));
                    }
                }
                catch
                {
                    //casting failed
                    throw new Exception(String.Format("Field {0} trying to cast to {1} failed! Something is amiss, tell David to check code",
                        tokenFullPath, typeof(T)));
                }

                //get all valid values as string for error message
                string validValuesString;

                validValuesString = "";
                for (int iValue = 0; iValue < m_validValues.Count; iValue++)
                {
                    if (iValue == m_validValues.Count - 1)
                    {
                        if (typeof(T) == typeof(string)) //there is no string.ToString()
                        {
                            validValuesString += "or " + "\"" + m_validValues[iValue] + "\"";
                        }
                        else
                        {
                            validValuesString += "or " + m_validValues[iValue].ToString();
                        }
                    }
                    else
                    {
                        if (typeof(T) == typeof(string)) //there is no string.ToString()
                        {
                            validValuesString += "\"" + m_validValues[iValue] + "\"" + ", ";
                        }
                        else
                        {
                            validValuesString += m_validValues[iValue].ToString() + ", ";
                        }
                    }
                }

                //finally do test of whether the value(s) is valid or not
                foreach (T value in values)
                {
                    if(!m_validValues.Contains(value))
                    {
                        wrongValues.Add(new List<string>() { tokenFullPath, value.ToString(), validValuesString });
                    }
                }
            }
        }


    }
}
