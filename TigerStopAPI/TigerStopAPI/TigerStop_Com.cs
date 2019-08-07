using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace TigerStopAPI
{
    /// <summary>
    /// Provides serial communication ability focused on a connection first communication infrastructure. Creates a FIFO command queue with
    /// each command executed in order after receiving an ack for the previous command's completion.
    /// </summary>
    public class TigerStop_Com
    {
        //  =  =  =  AUTORESET EVENTS  =  =  =
        AutoResetEvent serialAck = new AutoResetEvent(false);
        AutoResetEvent updateAck = new AutoResetEvent(false);

        //  =  =  =  BACKGROUND WORKERS  =  =  =
        BackgroundWorker bkgndCycle = new BackgroundWorker();

        //  =  =  =  BUFFERS  =  =  =
        private List<string> readBuffer = new List<string>();
        private List<byte[]> writeBuffer = new List<byte[]>();

        //  =  =  =  CONSTANTS  =  =  =
        const double HALTED = 0;
        const double ACCEL = 1;
        const double CONST_VEL = 2;
        const double DECEL = 3;
        const double DRIVE_DISABLED = 4;
        const double LASH = 5;
        const double WAIT_TO_MOVE = 6;
        const double EMERGENCY_STOP = 7;
        const double SLEEP = 8;
        const double MANUAL = 9;

        //  =  =  =  EVENT HANDLERS  =  =  =
        public event EventHandler SendData;
        private event EventHandler AddSetting;
        public event PropertyChangedEventHandler PropertyChanged;
        private event EventHandler UpdateSetting;
        public event EventHandler StopOperation;

        //  =  =  =  FIELDS  =  =  =
        private int settingIndex;
        public string serialNumber;
        public bool isRS232 = true;
        public bool isLastConnected = false;

        //  =  =  =  FLAGS  =  =  =
        //
        //  -  SETUP
        private bool isConnected = false;
        private bool isSetup = true;
        private bool isGettingSettings;
        private bool isDetectingTS = false;
        private bool isUpdatingSetting = false;
        //  -  MOVING
        private bool isMoving = false;
        private bool isMoveStart = false;
        private bool isHoming = false;
        private bool isMinMaxing = false;
        //  -  CYCLING
        private bool isCyclingTool = false;
        private bool isCycleStart = false;
        private bool isDmOff = false;
        private bool isDmOn = false;
        //  -  SCAN
        private bool isScanning = false;

        //  =  =  =  LISTS  =  =  =
        private List<DateTime> ackTimes = new List<DateTime>();
        private List<TimeSpan> mtAckTimes = new List<TimeSpan>();
        private List<double> settings = new List<double>();
        private List<double> scanMarks = new List<double>();

        //  =  =  =  READONLY COMMANDS  =  =  =
        private static readonly byte[] moveToolCommand = { 0x6d, 0x74, 0x0d, 0x0a };
        private static readonly byte[] moveHomeCommand = { 0x6d, 0x68, 0x0d, 0x0a };
        private static readonly byte[] moveStopCommand = { 0x6d, 0x73, 0x0d, 0x0a };
        private static readonly byte[] moveEStopCommand = { 0x6d, 0x65, 0x0d, 0x0a };
        private static readonly byte[] moveMinMaxCommand = { 0x6d, 0x6d, 0x0d, 0x0a };
        private static readonly byte[] positionQueryCommand = { 0x70, 0x0d, 0x0a };
        private static readonly byte[] scanCommand = { 0x02, 0x52, 0x00, 0x01, 0x80, 0x00, 0x00, 0x00, 0x0d, 0x0a };
        private static readonly byte[] statusQueryCommand = { 0x73, 0x0d, 0x0a };
        private static readonly byte[] serialQueryCommand = { 0x04, 0x31, 0x0d, 0x0a };

        //  -  SERIAL
        private SerialPort port = new SerialPort();
        public string comPortName;
        public int baudrate;

        //  =  =  =  STATIC COMMANDS  =  =  =
        private static byte[] loadSignalOn = { 0x77, 0x77, 0x20, 0x30, 0x78, 0x38, 0x30, 0x30, 0x20, 0x30, 0x78, 0x38, 0x30, 0x30, 0x0a, 0x0d };
        private static byte[] loadSignalOff = { 0x77, 0x77, 0x20, 0x30, 0x20, 0x30, 0x78, 0x38, 0x30, 0x30, 0x0a, 0x0d };

        //  =  =  =  STRUCTS  =  =  =

        /// <summary>
        /// A struct with fields to track the last command sent from the system to the machine and the time it was sent.
        /// </summary>
        private struct LastCommand
        {
            private byte[] command;
            private DateTime timesent;

            public LastCommand(byte[] comm, DateTime time)
            {
                command = comm;
                timesent = time;
            }

            public byte[] Command
            {
                get
                {
                    return command;
                }
                set
                {
                    command = value;
                }
            }

            public DateTime TimeSent
            {
                get
                {
                    return timesent;
                }
                set
                {
                    timesent = value;
                }
            }
        }

        /// <summary>
        /// A struct with fields to track the last acknowledgment received from the machine by the system and the time it was received.
        /// </summary>
        private struct LastAck
        {
            private string acknowledgement;
            private DateTime timerecieved;

            public LastAck(string ack, DateTime time)
            {
                acknowledgement = ack;
                timerecieved = time;
            }

            public string Acknowledgement
            {
                get
                {
                    return acknowledgement;
                }
                set
                {
                    acknowledgement = value;
                }
            }

            public DateTime TimeRecieved
            {
                get
                {
                    return timerecieved;
                }
                set
                {
                    timerecieved = value;
                }
            }
        }

        //   =  =  =  TRACKING VARIABLES  =  =  =
        private static LastCommand lastCommand = new LastCommand(null, DateTime.Now);
        private static LastAck lastAck = new LastAck(null, DateTime.Now);
        private static double position;
        private static double targetPosition;

        //  =  =  =  TIMEOUTS  =  =  =
        private TimeSpan timeout;
        private TimeSpan timeoutThreshold;
        private TimeSpan mtTimeout = TimeSpan.FromSeconds(10);   //We'll change it on initialization, but start with 10 seconds.
        private TimeSpan scanTimeout = TimeSpan.FromSeconds(15); //We'll change it on initialization, but start with 15 seconds.
        private TimeSpan homeTimeout = TimeSpan.FromSeconds(60); //We'll change it on initialization, but start with 60 seconds.

        //  =  =  =  GETTERS/SETTERS  =  =  =

        public bool IsOpen
        {
            get
            {
                return port.IsOpen;
            }
        }

        public bool IsConnected
        {
            get
            {
                return isConnected;
            }
        }

        public DateTime LastAckTime
        {
            get
            {
                return lastAck.TimeRecieved;
            }
        }

        public SerialPort Port
        {
            get
            {
                return port;
            }
            private set
            {
                this.port = value;
            }
        }

        public double Position
        {
            get
            {
                return position;
            }
            private set
            {
                position = value;
                NotifyPropertyChanged("Position");
            }
        }

        public List<double> Settings
        {
            get
            {
                return settings;
            }
        }

        public TimeSpan TimeOut
        {
            get
            {
                return this.timeout;
            }
            private set
            {
                this.timeout = value;
            }
        }

        //  =  =  =  CONSTRUCTORS  =  =  =

        //  -  SERIAL CONSTRUCTOR

        // --- public TigerStop_Com(string comPort, int baud) ---
        /// <summary>
        /// Basic parameterized constructor for TigerStop_Com.
        /// </summary>
        /// <param name="comPort"> A 'string' denoting the comport name of the serial port to connect to. </param>
        /// <param name="baud"> An 'int' denoting the baud rate to connect to the serial port at. </param>
        public TigerStop_Com(string comPort, int baud)
        {
            this.baudrate = baud;
            this.comPortName = comPort;

            port.DataReceived += SerialPort_DataReceived;
            AddSetting += SerialPort_AddSetting;
            UpdateSetting += SerialPort_UpdateSetting;
        }

        //  =  =  =  EVENT HANDLERS  =  =  =
        //
        // --- private void SerialPort_AddSetting(object sender, EventArgs e) ---
        /// <summary>
        /// This event handler is used specifically with the SerialPort_DataRecieved() event handler when the system is still in setup and obtaining all of the settings.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SerialPort_AddSetting(object sender, EventArgs e)
        {
            var setting = e as MessageEvent;
            double value;

            // If we see 'BAD' or 'INDEX' at all, we're beyond the index range and have found all of the settings.
            if (setting.Message.Contains("BAD") || setting.Message.Contains("INDEX"))
            {
                isGettingSettings = false;

                ClearPort();
            }
            else
            {
                if (double.TryParse(setting.Message, out value))
                {
                    settings.Add(value);
                }
            }
        }

        // --- private void SerialPort_UpdateSetting(object sender, EventArgs e) ---
        /// <summary>
        /// This event handler is used specifically with the SerialPort_DataReceived() event handler when the system is updating a specific setting.
        /// Upon retrieving the specific setting at 'settingIndex', if the returned value can be deciphered, its the new setting value, otherwise just keep the old value.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SerialPort_UpdateSetting(object sender, EventArgs e)
        {
            var setting = e as MessageEvent;
            double value;

            // If 'settings' is keeping track of a setting at the current 'settingIndex', then try to change it.
            if (settings.Count >= settingIndex - 1 && settings.Count > 0)
            {
                settings[settingIndex - 1] = double.TryParse(setting.Message, out value) ? value : settings[settingIndex - 1];
            }

            updateAck.Set();
        }

        // --- private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e) ---
        /// <summary>
        /// This is the main event handler, everything from the machine will be funneled through this event handler. Anytime the serial port buffer receives data, this
        /// the SerialPort.DataReceived event will fire and this event handler will be called to take in the data. This event handler is given its own thread to handle the
        /// data.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;

            try
            {
                string data = sp.ReadLine();

                //If we've taken care of all of the preliminary tasks, handle the data as necessary.
                if (!isSetup)
                {
                    HandleData(data);
                }
                //Otherwise, we're still in setup.
                else
                {
                    if (isDetectingTS)
                    {
                        int serialNum = 0;

                        try
                        {
                            if (int.TryParse(new string(data.Where(char.IsDigit).ToArray()), out serialNum))
                            {
                                isDetectingTS = false;

                                serialNumber = serialNum.ToString();

                                serialAck.Set();
                            }
                        }
                        catch
                        {
                            // We're just going to let the program sit out the timeout since we couldn't nail down a serial number.
                        }
                    }
                    else if (isGettingSettings)
                    {
                        AddSetting(this, new MessageEvent(data));
                    }
                    else if (isUpdatingSetting)
                    {
                        UpdateSetting(this, new MessageEvent(data));
                    }
                }
            }
            catch
            {

            }
        }

        // --- private void NotifyPropertyChanged(string property) --- 
        /// <summary>
        /// Basic property changed event handler.
        /// </summary>
        /// <param name="property"> The 'string' name of the property that was changed, to be sent out for others to identify and decide what to do with it. </param>
        private void NotifyPropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        //  =  =  =  METHODS  =  =  =
        //

        // --- private void HandleData(string data) ---
        /// <summary>
        /// Takes in a string of data from SerialPort_DataReceived() and parses it with any data in 'readBuffer' to determine if the machine has sent back an
        /// ack for us to decipher at any point.
        /// </summary>
        /// <param name="data"> A 'string' containing data from the serial port to send off to HandleAck() depending the systems current status. </param>
        private void HandleData(string data)
        {
            readBuffer.Add(data);

            if (!isScanning)
            {
                HandleAck();
            }
            else
            {
                long value = 0;

                // If we found the end of scan sequence, this is our ack for scan and need to stop.
                if (long.TryParse(string.Join("", readBuffer.ToArray()).Substring(4, string.Join("", readBuffer.ToArray()).Length - 5), out value))
                {
                    scanMarks.Add(Convert.ToDouble(value) / 1000);

                    readBuffer.Clear();
                }
                // The scan command is a altered move command, which means an 'MGS' will be seen and need to be cleared before the marks come in.
                else if (string.Join("", readBuffer.ToArray()).Contains("MGS"))
                {
                    readBuffer.Clear();
                }
                // Otherwise, we found a mark and need to add it to our marks list.
                else
                {
                    // If we found the end of the scan, Then we can handle the ack.
                    if (string.Join("", readBuffer.ToArray()).Contains("\0\0\0"))
                    {
                        HandleAck();
                    }

                    if (data.Contains("Err") || data.Contains("Scan"))
                    {
                        scanMarks.Clear();
                        readBuffer.Clear();
                        SendData(this, new MessageEvent("There was an error during the scan."));
                    }
                }
            }
        }

        // --- private void HandleAck() ---
        /// <summary>
        /// Once HandleData() collates the data taken in from SerialPort_DataReceived() and checks it for appropriate acks based on what the system is doing
        /// at the moment.
        /// </summary>
        private void HandleAck()
        {
            // If there isn't any kind of 'NACK' in the message, we have to treat it as a legitimate message.
            if (!string.Join("", readBuffer.ToArray()).Contains("NACK"))
            {
                if (!isScanning)
                {
                    // Started a move or too cycle.
                    if ((isMoveStart && string.Join("", readBuffer.ToArray()).Contains("MGS")) ^ (isCycleStart && string.Join("", readBuffer.ToArray()).Contains("MTS")))
                    {
                        if (isMoveStart)
                        {
                            isMoveStart = false;
                            isMoving = true;
                            
                            double.TryParse(string.Join("", readBuffer.ToArray()).TrimStart(new char[] { 'M', 'G', 'S', ' ' }).TrimEnd(new char[] { '\r', '\n' }), out targetPosition);
                        }
                        else
                        {
                            isCycleStart = false;
                            isCyclingTool = true;
                        }
                        
                        lastAck.Acknowledgement = string.Join("", readBuffer.ToArray());
                        lastAck.TimeRecieved = DateTime.Now;
                        
                        readBuffer.Clear();
                    }
                    // Finished a move or tool cycle.
                    else if (isMoving ^ isCyclingTool)
                    {
                        if (string.Join("", readBuffer.ToArray()).Contains("MGF"))
                        {
                            double finalPosition;
                            
                            lastAck.Acknowledgement = "MGF";
                            lastAck.TimeRecieved = DateTime.Now;

                            if (double.TryParse(string.Join("", readBuffer.ToArray()).TrimStart(new char[] { '\n', 'M', 'G', 'F', ' ' }).TrimEnd(new char[] { '\r', '\n' }), out finalPosition))
                            {
                                Position = finalPosition;
                            }
                            
                            SendData(this, new MessageEvent("MGF"));

                            isMoving = false;
                        }
                        else if (string.Join("", readBuffer.ToArray()).Contains("MTF"))
                        {
                            isDmOff = false;
                            isDmOn = false;
                            
                            lastAck.Acknowledgement = "MTF";
                            lastAck.TimeRecieved = DateTime.Now;
                            
                            SendData(this, new MessageEvent("MTF"));

                            isCyclingTool = false;
                        }
                        // If neither 'MGF' or 'MTF' is seen, a move or tool cycle is in progress.
                        else
                        {
                            lastAck.Acknowledgement = string.Join("", readBuffer.ToArray());
                            lastAck.TimeRecieved = DateTime.Now;

                            CheckSamePosition();
                        }
                        
                        readBuffer.Clear();

                        ClearCommand(false);

                        SendCommand();
                    }
                    // Is homing the device.
                    else if (isHoming)
                    {
                        if (string.Join("", readBuffer.ToArray()).Trim() == "0")
                        {
                            lastAck.Acknowledgement = "MHF";
                            lastAck.TimeRecieved = DateTime.Now;

                            SendData(this, new MessageEvent("MHF"));
                            
                            isHoming = false;

                            ClearCommand(false);
                            
                            SendCommand();
                        }
                        
                        readBuffer.Clear();
                    }
                    else if (!isMoving && !isCyclingTool)
                    {
                        lastAck.Acknowledgement = string.Join("", readBuffer.ToArray());
                        lastAck.TimeRecieved = DateTime.Now;
                        
                        if (!lastCommand.Command.SequenceEqual(scanCommand))
                        {
                            SendData(this, new MessageEvent(string.Join("", readBuffer.ToArray())));

                            if (lastCommand.Command.SequenceEqual(positionQueryCommand))
                            {
                                CheckSamePosition();
                            }

                            ClearCommand(false);
                        }
                        else
                        {
                            CheckSamePosition();

                            ClearCommand(false);
                        }
                        
                        readBuffer.Clear();

                        SendCommand();
                    }
                }
                else
                {
                    lastAck.Acknowledgement = string.Join("", readBuffer.ToArray());
                    lastAck.TimeRecieved = DateTime.Now;
                    
                    SendData(this, new MessageEvent(string.Join(",", scanMarks.ToArray())));
                    
                    scanMarks.Clear();
                    
                    isScanning = false;
                    
                    readBuffer.Clear();

                    ClearCommand(false);

                    SendCommand();
                }
            }
            else
            {
                // Clear the NACK out of the buffer.
                readBuffer.Clear();

                RetryCommand();
            }
        }

        // --- protected void QueueCommand(string command) ---
        /// <summary>
        /// This function is the main interface between the rest of the system and the machine. Any commands that need to be sent to the machine runs through this command.
        /// It takes a 'string' command to send to the machine. If the system already has commands queued up, it will add the command to the queue, otherwise it will call 
        /// SendCommand() to get the command processed immediately.
        /// </summary>
        /// <param name="command"> A 'string' that will be converted to a 'byte[]' command that will be sent to the machine. </param>
        protected void QueueCommand(string command)
        {
            byte[] cmd = CommandConverter(command);

            if (cmd.SequenceEqual(moveStopCommand))
            {
                WriteToSerial(moveStopCommand);
                ClearCommand(true);

                ChangeFlags(false);

                ClearPort();
            }
            else if (cmd.SequenceEqual(moveEStopCommand))
            {
                WriteToSerial(moveEStopCommand);
                ClearCommand(true);

                ChangeFlags(false);

                ClearPort();
            }
            else if (writeBuffer.Count != 0)
            {
                writeBuffer.Add(cmd);
            }
            else
            {
                writeBuffer.Add(cmd);
                SendCommand();
            }
        }

        // --- private byte[] CommandConverter ---
        /// <summary>
        /// Used specifically to convert 'string's into hex byte commands to send to the machine.
        /// </summary>
        /// <param name="input"> A 'string' that will be translated. </param>
        /// <returns name="command"> Returns a 'byte[]' to be used as a hex byte command by the machine. </returns>
        private byte[] CommandConverter(string input)
        {
            List<byte> command = new List<byte>();

            foreach (char c in input)
            {
                command.Add(Convert.ToByte(c));
            }

            // Add the \r\n delimiter for the amp.
            command.Add(0x0d);
            command.Add(0x0a);

            return command.ToArray();
        }

        // --- private void SendCommand() ---
        /// <summary>
        /// This function takes the first command from 'writeBuffer' and, depending on the command, sends it to the machine through the proper functions.
        /// </summary>
        private void SendCommand()
        {
            if (writeBuffer.Count != 0)
            {
                byte[] send = writeBuffer[0];

                if (send[0] == 0x06d)
                {
                    MoveCommand(send);
                }
                else if (send.SequenceEqual(scanCommand))
                {
                    TimeOut = Timeout.InfiniteTimeSpan;
                    isScanning = true;

                    WriteToSerial(scanCommand);
                }
                else
                {
                    WriteToSerial(send);
                }
            }
        }

        // --- private void MoveCommand(byte[] command) ---
        /// <summary>
        /// Called if the first command seen by SendCommand() is a move command, determine what kind of move command is being sent and set the appropriate flags
        /// and timeouts before sending the command to the machine.
        /// </summary>
        /// <param name="moveCommand"> A 'byte[]' command that will be used to determine which move command is being sent. </param>
        private void MoveCommand(byte[] moveCommand)
        {
            switch (moveCommand[1])
            {
                //Move go
                case 0x67:
                    TimeOut = Timeout.InfiniteTimeSpan;
                    isMoveStart = true;
                    WriteToSerial(moveCommand);
                    break;
                //Move home
                case 0x68:
                    TimeOut = homeTimeout;
                    isHoming = true;
                    WriteToSerial(moveHomeCommand);
                    break;
                //Move tool
                case 0x74:
                    TimeOut = mtTimeout;
                    isCycleStart = true;
                    WriteToSerial(moveToolCommand);
                    break;
                //Move Min-Max
                case 0x6d:
                    TimeOut = TimeSpan.FromSeconds(homeTimeout.TotalSeconds * 2.1);
                    isMinMaxing = true;
                    WriteToSerial(moveMinMaxCommand);
                    break;                    
            }
        }

        // --- protected void ClearCommand(bool allCommands) ---
        /// <summary>
        /// This function is used to clear out commands from the 'writeBuffer'. If 'allCommands' is 'true', it will clear all commands from the 'writeBuffer'.
        /// Otherwise, it will only clear the first command from 'writeBuffer'.
        /// </summary>
        /// <param name="allCommands"> A 'bool' that determines whether to clear the first command in the list or to clear all of the commands from the list. </param>
        protected void ClearCommand(bool allCommands)
        {
            if (allCommands)
            {
                SendData(this, new MessageEvent("There was an error."));
                writeBuffer.Clear();
            }
            else
            {
                if (writeBuffer.Count != 0)
                {
                    writeBuffer.Remove(writeBuffer[0]);
                }
            }
        }

        // --- private void WriteSerial(byte[] command) ---
        /// <summary>
        /// This function writes the byte[] command to the machine over the serial port. Also tracks the last command that was sent, in case we need send it again.
        /// </summary>
        /// <param name="command"> A 'byte' array of converted 'char' characters that the machine will recognize as an actionable command. </param>
        private void WriteToSerial(byte[] command)
        {
            port.Write(command, 0, command.Length);

            lastCommand.Command = command;
            lastCommand.TimeSent = DateTime.Now;
        }

        // --- private void WriteToSerialClean(byte[] command) ---
        /// <summary>
        /// This function writes the byte[] command to the machine over the serial port. It does not track the last command.
        /// </summary>
        /// <param name="command"> A 'byte' array of converted 'char' characters that the machine will recognize as an actionable command. </param>
        private void WriteToSerialClean(byte[] command)
        {
            port.Write(command, 0, command.Length);
        }

        // --- private void RetryCommand() ---
        /// <summary>
        /// This function sends the last command to the machine in the case the machine did not register or complete the last command. Caution is required
        /// when using this function as ‘lastCommand.Command’ may cause the machine to act unexpectedly, such as cycling the tool or moving when the user is unprepared.
        /// </summary>
        private void RetryCommand()
        {
            Thread.Sleep(100);

            ClearPort();

            // Make sure the proper flags are set.
            if (lastCommand.Command[0].Equals(0x6d) && lastCommand.Command[1].Equals(0x67))
            {
                isMoving = true;
            }
            else if (lastCommand.Command[0].Equals(0x6d) && lastCommand.Command[1].Equals(0x74))
            {
                isCyclingTool = true;
            }

            WriteToSerial(lastCommand.Command);
        }

        // --- private void GetSettings() ---
        /// <summary>
        /// This function is used to ask the machine for all of its settings and puts them into a list for future use.
        /// </summary>
        protected void GetSettings()
        {
            settings.Clear();

            isSetup = true;
            isGettingSettings = true;

            for (int i = 1; isGettingSettings; i++)
            {
                WriteToSerialClean(CommandConverter("d" + i));

                Thread.Sleep(50);
            }

            isSetup = false;

            ClearPort();
        }

        // --- private bool SamePosition() ---
        /// <summary>
        /// This function is used to determine if the position that the machine is at is the same as the last position that was queried. If the position
        /// is the same, the function returns 'true' if the position in the last acknowledgment is the same as the current position we know of. Otherwise,
        /// the function returns false.
        /// </summary>
        /// <returns name="samePostion"> Returns a bool denoting whether the machine is in the same place as the last time position was queried. </returns>
        private bool CheckSamePosition()
        {
            double newPosition = 0;
            bool samePosition = false;

            if (double.TryParse(lastAck.Acknowledgement.TrimStart(new char[] { 'M', 'G', 'F', 'S' }).TrimEnd(new char[] { '\r', '\n' }), out newPosition))
            {
                if (Position == newPosition)
                {
                    samePosition = false; ;
                }
                else
                {
                    samePosition = true;
                }

                Position = newPosition;
            }
            else
            {
                samePosition = true;
            }

            return samePosition;
        }

        // --- private void CheckMovement() ---
        /// <summary>
        /// This function is used while the machine is moving to double check that the machine is, in fact, moving like it was told to. 
        /// </summary>
        private void CheckMovement()
        {
            if (isMoving)
            {
                double status = 2;

                WriteToSerial(statusQueryCommand);

                Thread.Sleep(100);

                if (!double.TryParse(lastAck.Acknowledgement.TrimEnd(new char[] { '\r', '\n' }), out status))
                {
                    status = 2;
                }

                // If we're stopped, check if the machine is near the target position.
                if (status == DRIVE_DISABLED || status == HALTED)
                {
                    if (targetPosition - 0.004 <= Position && Position <= targetPosition + 0.004)
                    {
                        Position = targetPosition;
                        SendData(this, new MessageEvent("MGF"));
                    }
                    else
                    {
                        SendData(this, new MessageEvent("The system did not detect a proper move completion"));
                    }
                }
            }
            else
            {
                ClearPort();
            }
        }

        // --- protected void ClearPort() ---
        /// <summary>
        /// This function is used to clear out the serial port, reading anything currently in the serial port.
        /// </summary>
        protected void ClearPort()
        {
            port.ReadExisting();
        }

        // --- protected void ClosePort() ---
        /// <summary>
        /// Used to close the port when it is no longer in use.
        /// </summary>
        protected void ClosePort()
        {
            port.Close();
        }

        // --- protected void OpenPort() ---
        /// <summary>
        /// This function takes the stored com port name and baud rate and attempts to open a serial connection to the desired com port.
        /// </summary>
        protected void OpenPort()
        {
            try
            {
                port.PortName = comPortName;
                port.BaudRate = baudrate;
                port.StopBits = StopBits.One;
                port.DataBits = 8;
                port.Handshake = Handshake.None;
                port.DtrEnable = true;
                port.RtsEnable = true;

                port.Open();
            }
            catch
            {

            }
        }

        // --- protected bool DetectTigerStop() ---
        /// <summary>
        /// This function sends the serial command query to the machine to get a hold of its serial number. If the serial number is
        /// valid, then SerialPort_DataReceived() will signal the 'serialAck' to allow the function through and to return true.
        /// </summary>
        /// <returns name="detected"> Returns a 'bool' that signals whether or not the system detected a TigerStop machine on the other end of the connection. </returns>
        protected bool DetectTigerStop()
        {
            bool detected = false;

            isDetectingTS = true;

            WriteToSerialClean(serialQueryCommand);

            if (serialAck.WaitOne(1000))
            {
                detected = true;
            }
            else
            {
                detected = false;
            }

            return detected;
        }

        // --- protected bool CheckConnection() ---
        /// <summary>
        /// This function goes through all of the necessary checks that ensures the system is connected to a machine. If all of the checks pass
        /// a 'bool' is returned 'true' denoting that the system has successfully connected to the machine.
        /// </summary>
        /// <returns name="isConnected"> Returns a bool denoting whether we were able to connect to a machine with a valid enable code. </returns>
        protected bool CheckConnection()
        {
            bool isConnected = false;

            try
            {
                if (IsOpen)
                {
                    if (DetectTigerStop())
                    {
                        this.isLastConnected = true;
                        isConnected = true;
                        isSetup = false;
                    }
                    else
                    {
                        ClosePort();
                        this.isLastConnected = false;
                        isConnected = false;
                    }
                }
            }
            catch
            {
                isConnected = false;
            }

            return isConnected;
        }

        // --- protected void ChangeSetting(string command, int index) ---
        /// <summary>
        /// Takes a setting command and setting index to update the desired setting in 'settings' at 'index'.
        /// </summary>
        /// <param name="command"> A 'string' that will be sent to the machine to change the setting in the command to the desired value. </param>
        /// <param name="index"> An 'int' that denotes where in 'settings' the new setting value will be saved. </param>
        protected void ChangeSetting(string command, int index)
        {
            isSetup = true;
            isUpdatingSetting = true;

            settingIndex = index;

            WriteToSerialClean(CommandConverter(command));

            updateAck.Reset();

            updateAck.WaitOne(1000);

            isSetup = false;
            isUpdatingSetting = false;
        }

        // --- protected void ChangeSetting(string command) ---
        /// <summary>
        /// Sends a setting change command to the machine.
        /// </summary>
        /// <param name="command"> A 'string' that will be sent to the machine to change the setting in the command to the desired value. </param>
        protected void ChangeSetting(string command)
        {
            isSetup = true;
            isUpdatingSetting = true;
            
            // UpdateSetting() only cares about 'settingIndex' > 0.
            settingIndex = 0;

            WriteToSerial(CommandConverter(command));

            updateAck.Reset();

            if (updateAck.WaitOne(2000))
            {
                SendData(this, new MessageEvent("SetUp"));
            }

            isSetup = false;
            isUpdatingSetting = false;
        }

        // --- protected void InitializeTimeouts() ---
        /// <summary>
        /// Takes the currently saved timeout settings and initializes the timeouts to more expected timeouts.
        /// </summary>
        protected void InitializeTimeouts()
        {
            // Use the DmOff, TaON, TaOff, and DnOn settings to get an idea of how long the machine will wait for a cycle.
            mtTimeout = TimeSpan.FromSeconds(settings[55] + settings[56] + settings[57] + settings[58]);

            // Use the max length of the machine divided by home speed of five inches per second multiplied by two, for acceleration and deceleration times, to wait for home.
            homeTimeout = TimeSpan.FromSeconds((settings[9] / 5) * 2);
        }

        // --- protected LoadLight(bool on) ---
        /// <summary>
        /// This function is used to write to the serial port to have the machine turn on the load signal light according to the 'bool' input.
        /// </summary>
        /// <param name="on"> A 'bool' that denotes whether to turn on or turn off the load signal light on the machine. </param>
        protected void LoadLight(bool on)
        {
            switch (on)
            {
                case true:
                    WriteToSerialClean(loadSignalOn);
                    break;
                case false:
                    WriteToSerialClean(loadSignalOff);
                    break;
            }
        }

        // --- protected static List<KeyValuePair<string, int>> FindConnections() ---
        /// <summary>
        /// Opens each of the available comports at a number of baudrates and checks each for a potential connection to a TigerStop amp.
        /// </summary>
        /// <returns name="connections"> A 'List' of 'KeyValuePair's with comport names as 'string' keys and baudrate 'int' values of potential connections. </returns>
        protected static List<KeyValuePair<string, int>> FindConnections()
        {
            List<KeyValuePair<string, int>> connections = new List<KeyValuePair<string, int>>();
            int serial = 0;
            int[] baudrates = new int[5] { 9600, 19200, 38400, 57600, 115200 };
            SerialPort searchPort = new SerialPort();

            searchPort.StopBits = StopBits.One;
            searchPort.DataBits = 8;
            searchPort.Handshake = Handshake.None;
            searchPort.DtrEnable = true;
            searchPort.RtsEnable = true;
            searchPort.ReadTimeout = 1000;

            foreach (string p in SerialPort.GetPortNames())
            {
                searchPort.PortName = p;

                for (int i = 0; i < baudrates.Length; i++)
                {
                    searchPort.BaudRate = baudrates[i];

                    try
                    {
                        searchPort.Open();

                        searchPort.Write(serialQueryCommand, 0, serialQueryCommand.Length);

                        Thread.Sleep(100);

                        // A response as a readable integer is enough to believe a TigerStop amp is on the other end of the connection.
                        if (int.TryParse(new string(searchPort.ReadLine().Where(char.IsDigit).ToArray()), out serial))
                        {
                            connections.Add(new KeyValuePair<string, int>(p, baudrates[i]));
                            searchPort.Close();
                        }
                        else
                        {
                            searchPort.Close();
                        }
                    }
                    // Expect to catch a lot of timeout exceptions.
                    catch
                    {
                        searchPort.Close();
                    }
                }
            }

            return connections;
        }

        // --- private void ChangeFlags(bool change) ---
        /// <summary>
        /// Changes all of the 'bool' flags to the value of 'change'.
        /// </summary>
        /// <param name="change"> A 'bool' representing the value to change all of the flags to. </param>
        private void ChangeFlags(bool change)
        {
            isMoving = change;
            isCyclingTool = change;
            isScanning = change;
            isMoveStart = change;
            isCycleStart = change;
            isDmOff = change;
            isDmOn = change;
        }
    }
}
