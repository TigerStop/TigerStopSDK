using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace TigerStopAPI
{
    /// <summary>
    /// TigerStop_IO inherits TigerStop_Com and wraps its serial communication ability in simpler, easy to manage functions. 
    /// This class is the main interface between outside entities and the machine, wrapping the more esoteric machine specific 
    /// commands into simple functions that inform the caller if they were able to successfully complete the desired command.
    /// </summary>
    public class TigerStop_IO : TigerStop_Com
    {
        //  =  =  =  AUTORESET EVENTS  =  =  =
        private AutoResetEvent ackEvent = new AutoResetEvent(false);
        private AutoResetEvent movingEvent = new AutoResetEvent(false);
        private AutoResetEvent cyclingEvent = new AutoResetEvent(false);
        private AutoResetEvent deadmanOffEvent = new AutoResetEvent(false);
        private AutoResetEvent deadmanOnEvent = new AutoResetEvent(false);
        private AutoResetEvent homingEvent = new AutoResetEvent(false);
        private AutoResetEvent measureEvent = new AutoResetEvent(false);

        //  =  =  =  EVENTS  =  =  =
        public EventHandler IO_Error;

        //  =  =  =  FIELDS  =  =  =
        private string ackMessage;
        private List<string> settingNames = new List<string>
        {
            "P Gain", "I Gain", "D Gain", "Vel In", "Vel Out", "Acc In", "Acc Out", "Dec In", "Dec Out", "Lim Max",                         // 1 - 10
            "Lim Min", "IOP Baud", "Scale", "Dither", "Debug", "Clamp 1", "Clamp 2", "Position", "PrtType", "PrtBaud",                      // 11 - 20
            "CommBaud", "JetOffset", "Kerf", "Head Cut", "Tail Cut", "Outfeed", "Backoff", "Retract", "Ret Offset", "Feed Haz",             // 21 - 30
            "Load Off", "Max SPL", "Opti Score", "Opti Time", "Opti Pen", "Move Delay", "Lash", "SM Table", "UnLoad", "Motor Type",         // 31 - 40
            "ME Zero", "Language", "Con Sleep", "MMEnable", "Contrast", "Prt Names", "Prt Cuts", "Ret Type", "IOReadMask", "IOWritMask",    // 41 - 50
            "PresetType", "Waste First", "ConPW", "ClampOn_D", "SawOn_D", "DMOff_TO", "TAOn_TO", "TAOff_TO", "DMOn_TO", "ClampOff_D",       // 51 - 60
            "SawCyc_D", "RSD_Rdy_TO", "Timer10", "ComPanel", "CrossCal", "CrossAlarm", "HandyOpt", "Waste", "DropBox", "Defect",            // 61 - 70
            "L Range", "L Count", "Laser ME", "L Limit", "L Ref", "Cld Sty", "S cpi", "L cpi", "Pnt Dly", "TH LV M",                        // 71 - 80
            "TH LV B", "Can Dly", "Inf", "Pet Clear", "Pet Dim", "Banana1", "Banana2", "SC Rel", "TH HV M", "TH HV B",                      // 81 - 90
            "StallMEP", "StallMES", "Run MEP", "Run MES", "RateGain", "RateThes", "SetPntSys", "Timer", "FindVel", "FindErr",               // 91 - 100
            "DrillMode", "MMRatio", "IR Type", "SI_RES3", "Mtr CPR", "Mtr Poles", "TTenable", "DrvAccLmt", "TM Offset", "TM Enable",        // 101 - 110
            "Fast Cal", "Fast Unit", "QFilter", "DFilter", "EFilter", "Brk TYPE", "PF_Up_Pos", "Rev Jog", "VJ Font", "Crayon",              // 111 - 120
            "UV_IR_Off", "PF_Offset", "D Margin"                                                                                            // 121 - 123
        };

        //  =  =  =  GETTERS/SETTERS  =  =  =
        public string AckMessage
        {
            get
            {
                return ackMessage;
            }
            private set
            {
                this.ackMessage = value;
            }
        }

        public List<string> SettingNames
        {
            get
            {
                return settingNames;
            }
            private set
            {
                this.settingNames = value;
            }
        }

        //  =  =  =  CONSTRUCTORS  =  =  =

        // --- public TigerStop_IO(string comPort, int baud) ---
        /// <summary>
        /// A basic parameterized constructor for TigerStop_IO that sends 'comPort' and 'baud' to TigerStop_Com()
        /// and opens a serial connection with the given 'comPort' and 'baud' and ensures that the connection
        /// is successfully made.
        /// </summary>
        /// <param name="comPort"> A 'string' denoting the comport name that will be sent to the TigerStop_Com() constructor. </param>
        /// <param name="baud"> An 'int' denoting the baud rate to connect to the serial port at that will be sent to the TigerStop_Com constructor. </param>
        public TigerStop_IO(string comPort, int baud) : base(comPort, baud)
        {
            base.OpenPort();

            if (base.CheckConnection())
            {
                SendData += Com_MessageReceived;
            }
        }

        //  =  =  =  EVENTS  =  =  =

        // --- private void Com_MessageReceived(object sender, EventArgs e) ---
        /// <summary>
        /// This event listens for messages from TigerStop_Com that can be used by TigerStop_IO.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Com_MessageReceived(object sender, EventArgs e)
        {
            var ack = e as MessageEvent;

            // Copy the message for the waiting event to look at.
            AckMessage = ack.Message;

            // The system needs to look out for various kinds of acks; a moving ack, "MGF", a cycling ack, "MTF", two dead man acks "DMS" and "DMF", 
            // a homing ack, "MHF", a measuring ack, "TMF", or a general ack.
            if (ack.Message.Contains("MGF"))
            {
                movingEvent.Set();
            }
            else if (ack.Message.Contains("MTF"))
            {
                cyclingEvent.Set();
            }
            else if (ack.Message.Contains("DMS"))
            {
                deadmanOffEvent.Set();
            }
            else if (ack.Message.Contains("DMF"))
            {
                deadmanOnEvent.Set();
            }
            else if (ack.Message.Contains("MHF"))
            {
                homingEvent.Set();
            }
            else if (ack.Message.Contains("TMF"))
            {
                measureEvent.Set();
            }
            else
            {
                ackEvent.Set();
            }
        }

        //  =  =  =  METHODS  =  =  =

        // --- public bool MoveTo(double position) ---
        /// <summary>
        /// Sends a move command to the machine to move to the desired position waiting for the move to finish.
        /// </summary>
        /// <param name="position"> A 'double' denoting the desired position to move to. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the move command was successfully completed. </returns>
        public bool MoveTo(double position)
        {
            bool isDone = false;

            base.QueueCommand("mg" + position);

            movingEvent.Reset();
            movingEvent.WaitOne();

            isDone = true;

            return isDone;
        }

        // --- public bool MoveTo(double position, int timeout) ---
        /// <summary>
        /// Sends a move command to the machine to move to the desired position, waiting for the move to finish or for the given timeout duration before returning.
        /// </summary>
        /// <param name="position"> A 'double' denoting the desired position to move to. </param>
        /// <param name="timeout"> An 'int' denotes the number of milliseconds to timeout on. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the move command was successfully completed. </returns>
        public bool MoveTo(double position, int timeout)
        {
            bool isDone = false;

            base.QueueCommand("mg" + position);

            movingEvent.Reset();
            if (!movingEvent.WaitOne(timeout))
            {
                isDone = false;
            }
            else
            {
                isDone = true;
            }

            return isDone;
        }

        // --- public bool MoveTo(double position, ref BackgroundWorker b) ---
        /// <summary>
        /// Sends a move command to the machine to move to the desired position waiting for the move to finish.
        /// </summary>
        /// <param name="position"> A 'double' denoting the desired position to move to. </param>
        /// <param name="b"> A 'BackgroundWorker' that is running MoveTo() that may signal an impending cancellation. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the move command was successfully completed. </returns>
        public bool MoveTo(double position, ref BackgroundWorker b)
        {
            bool isDone = false;

            base.QueueCommand("mg" + position);

            if (b.CancellationPending)
            {
                return false;
            }

            movingEvent.Reset();
            movingEvent.WaitOne();

            if (b.CancellationPending)
            {
                return false;
            }

            isDone = true;

            return isDone;
        }

        // --- public bool MoveTo(double position, int timeout, ref BackgroundWorker b) ---
        /// <summary>
        /// Sends a move command to the machine to move to the desired position, waiting for the move to finish or for the given timeout duration before returning.
        /// </summary>
        /// <param name="position"> A 'double' denoting the desired position to move to. </param>
        /// <param name="timeout"> An 'int' that denotes the number of milliseconds to timeout on. </param>
        /// <param name="b"> A 'BackgroundWorker' that is running MoveTo() that may signal an impending cancellation. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the move command was successfully completed. </returns>
        public bool MoveTo(double position, int timeout, ref BackgroundWorker b)
        {
            bool isDone = false;

            base.QueueCommand("mg" + position);

            if (b.CancellationPending)
            {
                return false;
            }

            movingEvent.Reset();
            if (!movingEvent.WaitOne(timeout))
            {
                isDone = false;
            }
            else
            {
                isDone = true;
            }

            if (b.CancellationPending)
            {
                return false;
            }

            return isDone;
        }

        // --- public bool MoveTo(double position) ---
        /// <summary>
        /// Sends a move command to the machine to move to the desired position waiting for the move to finish.
        /// </summary>
        /// <param name="position"> A 'string' denoting the desired position to move to. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the move command was successfully completed. </returns>
        public bool MoveTo(string position)
        {
            bool isDone = false;

            base.QueueCommand("mg" + position);

            movingEvent.Reset();
            movingEvent.WaitOne();

            isDone = true;

            return isDone;
        }

        // --- public bool MoveTo(double position, int timeout) ---
        /// <summary>
        /// Sends a move command to the machine to move to the desired position, waiting for the move to finish or for the given timeout duration before returning.
        /// </summary>
        /// <param name="position"> A 'string' denoting the desired position to move to. </param>
        /// <param name="timeout"> An 'int' that denotes the number of milliseconds to timeout on. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the move command was successfully completed. </returns>
        public bool MoveTo(string position, int timeout)
        {
            bool isDone = false;

            base.QueueCommand("mg" + position);

            movingEvent.Reset();
            if (!movingEvent.WaitOne(timeout))
            {
                isDone = false;
            }
            else
            {
                isDone = true;
            }

            return isDone;
        }

        // --- public bool MoveTo(string position, ref BackgroundWorker b) ---
        /// <summary>
        /// Sends a move command to the machine to move to the desired position waiting for the move to finish.
        /// </summary>
        /// <param name="position"> A 'string' denoting the desired position to move to. </param>
        /// <param name="b"> A 'BackgroundWorker' that is running MoveTo() that may signal an impending cancellation. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the move command was successfully completed. </returns>
        public bool MoveTo(string position, ref BackgroundWorker b)
        {
            bool isDone = false;

            base.QueueCommand("mg" + position);

            if (b.CancellationPending)
            {
                return false;
            }

            movingEvent.Reset();
            movingEvent.WaitOne();

            if (b.CancellationPending)
            {
                return false;
            }

            isDone = true;

            return isDone;
        }

        // --- public bool MoveTo(string position, int timeout, ref BackgroundWorker b) ---
        /// <summary>
        /// Sends a move command to the machine to move to the desired position, waiting for the move to finish or for the given timeout duration before returning.
        /// </summary>
        /// <param name="position"> A 'string' denoting the desired position to move to. </param>
        /// <param name="timeout"> An 'int' denotes the number of milliseconds to timeout on. </param>
        /// <param name="b"> A 'BackgroundWorker' that is running MoveTo() that may signal an impending cancellation. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the move command was successfully completed. </returns>
        public bool MoveTo(string position, int timeout, ref BackgroundWorker b)
        {
            bool isDone = false;

            base.QueueCommand("mg" + position);

            if (b.CancellationPending)
            {
                return false;
            }

            movingEvent.Reset();
            if (!movingEvent.WaitOne(timeout))
            {
                isDone = false;
            }
            else
            {
                isDone = true;
            }

            if (b.CancellationPending)
            {
                return false;
            }

            return isDone;
        }

        // --- public bool HomeDevice() ---
        /// <summary>
        /// Runs the home routine to return the machine to the home position, and enable the drive if it is disabled.
        /// </summary>
        public void HomeDevice()
        {
            homingEvent.Reset();

            base.QueueCommand("mh");

            homingEvent.WaitOne(base.TimeOut);
        }

        // --- public void CycleTool() ---
        /// <summary>
        /// Sends a cycle tool command to the machine waiting until the tool cycle is finished.
        /// </summary>
        /// <returns name="isDone"> A 'bool' that signals whether the cycle command was successfully completed. </returns>
        public bool CycleTool()
        {
            bool isDone = false;

            base.QueueCommand("mt");

            cyclingEvent.Reset();
            cyclingEvent.WaitOne();

            isDone = true;

            return isDone;
        }

        // --- public void CycleTool() ---
        /// <summary>
        /// Sends a cycle tool command to the machine, waiting for the tool cycle to finish or for the given timeout duration before finishing.
        /// </summary>
        /// <param name="timeout"> An 'int' that denotes the number of milliseconds to timeout on. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the cycle command was successfully completed. </returns>
        public bool CycleTool(int timeout)
        {
            bool isDone = false;

            base.QueueCommand("mt");

            cyclingEvent.Reset();
            if (!cyclingEvent.WaitOne((int)timeout))
            {
                isDone = false;
            }
            else
            {
                isDone = true;
            }


            return isDone;
        }

        // --- public bool CycleTool(ref BackgroundWorker b) ---
        /// <summary>
        /// Sends a cycle tool command to the machine waiting until the tool cycle is finished. 
        /// </summary>
        /// <param name="b"> A 'BackgroundWorker' that is running CycleTool() that may signal an impending cancellation. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the cycle command was successfully completed. </returns>
        public bool CycleTool(ref BackgroundWorker b)
        {
            bool isDone = false;

            base.QueueCommand("mt");

            if (b.CancellationPending)
            {
                return false;
            }

            cyclingEvent.Reset();
            cyclingEvent.WaitOne();

            if (b.CancellationPending)
            {
                return false;
            }

            isDone = true;

            return isDone;
        }

        // --- public bool CycleTool(ref BackgroundWorker b) ---
        /// <summary>
        /// Sends a cycle tool command to the machine waiting until the tool cycle is finished. 
        /// </summary>
        /// <param name="timeout"> An 'int' that denotes the number of milliseconds to timeout on. </param>
        /// <param name="b"> A 'BackgroundWorker' that is running CycleTool() that may signal an impending cancellation. </param>
        /// <returns name="isDone"> A 'bool' that signals whether the cycle command was successfully completed. </returns>
        public bool CycleTool(int timeout, ref BackgroundWorker b)
        {
            bool isDone = false;

            base.QueueCommand("mt");

            if (b.CancellationPending)
            {
                return false;
            }

            cyclingEvent.Reset();
            if (!cyclingEvent.WaitOne((int)timeout))
            {
                isDone = false;
            }
            else
            {
                isDone = true;
            }

            if (b.CancellationPending)
            {
                return false;
            }


            return isDone;
        }

        // --- public string GetSetting(int settingIndex) ---
        /// <summary>
        /// Returns the value of a desired setting at 'settingIndex'.
        /// </summary>
        /// <param name="settingIndex"> An 'int' that relates to the index of the desired setting to retrieve. </param>
        /// <returns name="settingValue"> A 'string' containing the value of the setting at 'settingIndex' or 'null' if a response wasn't received in an expected time frame. </returns>
        public string GetSetting(int settingIndex)
        {
            string settingValue = null;

            base.QueueCommand("d" + settingIndex);

            ackEvent.Reset();

            if (ackEvent.WaitOne(2000))
            {
                settingValue = AckMessage;
            }

            return settingValue;
        }

        // --- public string GetSetting(string settingName) ---
        /// <summary>
        /// Returns the value of the desired setting whose name matches 'settingName'.
        /// </summary>
        /// <param name="settingName"> A 'string' that matches the name of the desired setting to retrieve. </param>
        /// <returns name="settingValue"> A 'string' containing the value of the setting whose name matches 'settingName' or 'null' if a response wasn't received in an expected time frame. </returns>
        public string GetSetting(string settingName)
        {
            string settingValue = null;

            if (settingNames.Contains(settingName))
            {
                base.QueueCommand("d" + (settingNames.IndexOf(settingName) + 1));

                ackEvent.Reset();

                if (ackEvent.WaitOne(2000))
                {
                    settingValue = AckMessage;
                }
            }

            return settingValue;
        }

        // --- public string GetSetting(int settingIndex, int timeout) ---
        /// <summary>
        /// Returns the value of the desired setting at 'settingIndex', waiting for 'timeout's duration for a response.
        /// </summary>
        /// <param name="settingIndex"> An 'int' that relates to the index of the desired setting to retrieve. </param>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <returns name="settingValue"> A 'string' containing the value of the setting at 'settingIndex' or 'null' if a response was not received in the expected time frame of 'timeout'. </returns>
        public string GetSetting(int settingIndex, int timeout)
        {
            string settingValue = null;

            base.QueueCommand("d" + settingIndex);

            ackEvent.Reset();
            if (ackEvent.WaitOne(timeout))
            {
                settingValue = AckMessage;
            }

            return settingValue;
        }

        // --- public string GetSetting(string settingName, int timeout) ---
        /// <summary>
        /// Returns the value of the setting whose name matches 'settingName', waiting for 'timeout's duration for a response.
        /// </summary>
        /// <param name="settingName"> A 'string' that matches the name of the desired setting to retrieve. </param>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <returns name="settingValue"> A 'string' containing the value of the setting whose name matches 'settingName' or 'null' if a response wasn't received in the expected time frame of 'timeout'. </returns>
        public string GetSetting(string settingName, int timeout)
        {
            string settingValue = null;

            if (settingNames.Contains(settingName))
            {
                base.QueueCommand("d" + settingNames.IndexOf(settingName));

                ackEvent.Reset();

                if (ackEvent.WaitOne(timeout))
                {
                    settingValue = AckMessage;
                }
            }

            return settingValue;
        }

        // --- public void Stop() ---
        /// <summary>
        /// Sends a stop command to the machine, ending any action its currently in the middle of.
        /// </summary>
        public void Stop()
        {
            base.QueueCommand("ms");
        }

        // --- public void EmergencyStop() ---
        /// <summary>
        /// Sends an emergency stop command to the machine, ending any action its currently in the middle of and disabling the machine's drive.
        /// </summary>
        public void EmergencyStop()
        {
            base.QueueCommand("me");
        }

        // --- public static List<KeyValuePair<string, int>> Connections() ---
        /// <summary>
        /// Searches through all available com ports and baud rates to find potential connections by asking for serial numbers from machines that
        /// might be on the other end of the connection.
        /// </summary>
        /// <returns name="connections"> A 'Dictionary' where a 'string' comport name key has an 'int' baud rate value. </returns>
        public static List<KeyValuePair<string, int>> Connections()
        {
            return FindConnections();
        }

        // --- public string[] GetAnalog() ---
        /// <summary>
        /// Returns the analog values tracked by the amp.
        /// </summary>
        /// <returns name="values"> A 'string' array of 5 entries that hold each of the 5 analog values tracked by the amp. </returns>
        public string[] GetAnalog()
        {
            string[] values = new string[5];

            base.QueueCommand("a");

            for (int i = 0; i < 5; i++)
            {
                ackEvent.Reset();

                if (ackEvent.WaitOne(500))
                {
                    values[i] = AckMessage;
                }
            }

            return values;
        }

        // --- public string GetAnalog(int subCommand) ---
        /// <summary>
        /// Returns the analog value related to the analog subcommand, 1-5, 'subCommand' matches.
        /// </summary>
        /// <param name="subCommand"> An 'int' that represents the desired subcommand value to retrieve. </param>
        /// <returns name="analogValue"> A 'string' that represents the returned value of the subcommand at 'subCommand' or 'null' if a response is not received in an expected time frame. </returns>
        public string GetAnalog(int subCommand)
        {
            string analogValue = null;

            base.QueueCommand("a" + subCommand);

            ackEvent.Reset();

            if (ackEvent.WaitOne(2000))
            {
                analogValue = AckMessage;
            }

            return analogValue;
        }

        // --- public string GetAnalog(int subCommand, int timeout) ---
        /// <summary>
        /// Returns the analog value related to the analog subcommand, 1-5, 'subCommand' matches. Waiting for the duration of 'timeout' for a response.
        /// </summary>
        /// <param name="subCommand"> An 'int' that represents the desired subcommand value to retrieve. </param>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <returns name="analogValue"> A 'string' that represents the returned value of the subcommand at 'subCommand' or 'null' if a response is not received in the expected time frame of 'timeout'. </returns>
        public string GetAnalog(int subCommand, int timeout)
        {
            string analogValue = null;

            base.QueueCommand("a" + subCommand);

            ackEvent.Reset();

            if (ackEvent.WaitOne(timeout))
            {
                analogValue = AckMessage;
            }

            return analogValue;
        }

        // --- public string GetAnalog(string subCommand) ---
        /// <summary>
        /// Returns the analog value related to the analog subcommand, 1-5, 'subCommand' matches.
        /// </summary>
        /// <param name="subCommand"> An 'string' that represents the desired subcommand value to retrieve. </param>
        /// <returns name="analogValue"> A 'string' that represents the returned value of the subcommand at 'subCommand' or 'null' if a response is not received in an expected time frame. </returns>
        public string GetAnalog(string subCommand)
        {
            string analogValue = null;

            base.QueueCommand("a" + subCommand);

            ackEvent.Reset();

            if (ackEvent.WaitOne(2000))
            {
                analogValue = AckMessage;
            }

            return analogValue;
        }

        // --- public string GetAnalog(string subCommand, int timeout) ---
        /// <summary>
        /// Returns the analog value related to the analog subcommand, 1-5, 'subCommand' matches. Waiting for the duration of 'timeout' for a response.
        /// </summary>
        /// <param name="subCommand"> An 'string' that represents the desired subcommand value to retrieve. </param>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <returns name="analogValue"> A 'string' that represents the returned value of the subcommand at 'subCommand' or 'null' if a response is not received in the expected time frame of 'timeout'. </returns>
        public string GetAnalog(string subCommand, int timeout)
        {
            string analogValue = null;

            base.QueueCommand("a" + subCommand);

            ackEvent.Reset();

            if (ackEvent.WaitOne(timeout))
            {
                analogValue = AckMessage;
            }

            return analogValue;
        }

        // --- public string[] GetLog() ---
        /// <summary>
        /// Returns the 20 most recent command and error log entries.
        /// </summary>
        /// <returns name="log"> A 'string' array of 20 entries that hold each of the 20 command and error log entries. </returns>
        public string[] GetLog()
        {
            string[] log = new string[20];

            base.QueueCommand("b");

            for (int i = 0; i < 20; i++)
            {
                ackEvent.Reset();

                if (ackEvent.WaitOne(500))
                {
                    log[i] = AckMessage;
                }
            }

            return log;
        }

        // --- public string[] GetLog(int logIndex) ---
        /// <summary>
        /// Returns the 20 most recent command and error log entries starting at 'logIndex' and going back.
        /// </summary>
        /// <param name="logIndex"> An 'int' that designates the log index to start from. </param>
        /// <returns name="log"> A 'string' array of 20 entries that hold each of the command and error log entries starting at 'logIndex'. </returns>
        public string[] GetLog(int logIndex)
        {
            string[] log = new string[20];

            base.QueueCommand("b" + logIndex);

            for (int i = 0; i < 20; i++)
            {
                ackEvent.Reset();

                if (ackEvent.WaitOne(500))
                {
                    log[i] = AckMessage;
                }
            }

            return log;
        }

        // --- public string[] GetLog(string logIndex) ---
        /// <summary>
        /// Returns the 20 most recent command and error log entries starting at 'logIndex' and going back.
        /// </summary>
        /// <param name="logIndex"> An 'string' that designates the log index to start from. </param>
        /// <returns name="log"> A 'string' array of 20 entries that hold each of the command and error log entries starting at 'logIndex'. </returns>
        public string[] GetLog(string logIndex)
        {
            string[] log = new string[20];

            base.QueueCommand("b" + logIndex);

            for (int i = 0; i < 20; i++)
            {
                ackEvent.Reset();

                if (ackEvent.WaitOne(500))
                {
                    log[i] = AckMessage;
                }
            }

            return log;
        }

        // --- public string[] GetCounters() ---
        /// <summary>
        /// Returns the 25 counter values tracked by the amp.
        /// </summary>
        /// <returns name="counters"> A 'string' array of 25 entries that hold each of the 25 counter values tracked by the amp. </returns>
        public string[] GetCounter()
        {
            string[] counters = new string[25];

            base.QueueCommand("c");

            for (int i = 0; i < 25; i++)
            {
                ackEvent.Reset();

                if (ackEvent.WaitOne(500))
                {
                    counters[i] = AckMessage;
                }
            }

            return counters;
        }

        // --- public string GetCounter(int subCommand) ---
        /// <summary>
        /// Returns the counter value related to the counter subcommand, 1-25, 'subCommand' matches.
        /// </summary>
        /// <param name="subCommand"> An 'int' that represents the desired subcommand value to retrieve. </param>
        /// <returns name="counterValue"> A 'string' that represents the returned value of the subcommand at 'subCommand' or 'null' if a response is not received in an expected time frame. </returns>
        public string GetCounter(int subCommand)
        {
            string counterValue = null;

            base.QueueCommand("c" + subCommand);

            ackEvent.Reset();

            if (ackEvent.WaitOne(2000))
            {
                counterValue = AckMessage;
            }

            return counterValue;
        }

        // --- public string GetCounter(int subCommand, int timeout) ---
        /// <summary>
        /// Returns the counter value related to the counter subcommand, 1-25, 'subCommand' matches. Waiting for the duration of 'timeout' for a response.
        /// </summary>
        /// <param name="subCommand"> An 'int' that represents the desired subcommand value to retrieve. </param>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <returns name="counterValue"> A 'string' that represents the returned value of the subcommand at 'subCommand' or 'null' if a response is not received in the expected time frame of 'timeout'. </returns>
        public string GetCounter(int subCommand, int timeout)
        {
            string counterValue = null;

            base.QueueCommand("c" + subCommand);

            ackEvent.Reset();

            if (ackEvent.WaitOne(timeout))
            {
                counterValue = AckMessage;
            }

            return counterValue;
        }

        // --- public string GetCounter(string subCommand) ---
        /// <summary>
        /// Returns the counter value related to the counter subcommand, 1-25, 'subCommand' matches.
        /// </summary>
        /// <param name="subCommand"> A 'string' that represents the desired subcommand value to retrieve. </param>
        /// <returns name="counterValue"> A 'string' that represents the returned value of the subcommand at 'subCommand' or 'null' if a response is not received in an expected time frame. </returns>
        public string GetCounter(string subCommand)
        {
            string counterValue = null;

            base.QueueCommand("c" + subCommand);

            ackEvent.Reset();

            if (ackEvent.WaitOne(2000))
            {
                counterValue = AckMessage;
            }

            return counterValue;
        }

        // --- public string GetCounter(string subCommand, int timeout) ---
        /// <summary>
        /// Returns the counter value related to the counter subcommand, 1-25, 'subCommand' matches. Waiting for the duration of 'timeout' for a response.
        /// </summary>
        /// <param name="subCommand"> A 'string' that represents the desired subcommand value to retrieve. </param>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <returns name="counterValue"> A 'string' that represents the returned value of the subcommand at 'subCommand' or 'null' if a response is not received in the expected time frame of 'timeout'. </returns>
        public string GetCounter(string subCommand, int timeout)
        {
            string counterValue = null;

            base.QueueCommand("c" + subCommand);

            ackEvent.Reset();

            if (ackEvent.WaitOne(timeout))
            {
                counterValue = AckMessage;
            }

            return counterValue;
        }

        // --- public string GetPosition() ---
        /// <summary>
        /// Returns the current position of the machine.
        /// </summary>
        /// <returns name="pos"> A 'double' that represents the returned current position from the machine or 'NaN' if no response is received in an expected time frame. </returns>
        public double GetPosition()
        {
            double pos = 0.00;

            base.QueueCommand("p");

            ackEvent.Reset();

            if (ackEvent.WaitOne(2000))
            {
                pos = Convert.ToDouble(AckMessage.TrimEnd('\r'));
            }
            else
            {
                pos = double.NaN;
            }

            return pos;
        }

        // --- public double GetPosition(int timeout) ---
        /// <summary>
        /// Returns the current position of the machine, waiting for the duration of 'timeout' for a response.
        /// </summary>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <returns name="pos"> A 'double' that represents the returned current position from the machine or 'NaN' if no response is received in the expected time frame of 'timeout'. </returns>
        public double GetPosition(int timeout)
        {
            double pos = 0.00;

            base.QueueCommand("p");

            ackEvent.Reset();

            if (ackEvent.WaitOne(timeout))
            {
                pos = Convert.ToDouble(AckMessage.TrimEnd('\r'));
            }
            else
            {
                pos = double.NaN;
            }

            return pos;
        }

        // --- public int GetStatus() ---
        /// <summary>
        /// Returns the current status of the machine.
        /// </summary>
        /// <returns name="state"> An 'int' that represents the returned status of the machine or '-1' if no response is received in an expected time frame. </returns>
        public int GetStatus()
        {
            int state = -1;

            base.QueueCommand("s");

            ackEvent.Reset();

            if (ackEvent.WaitOne(2000))
            {
                state = Convert.ToInt32(AckMessage);
            }

            return state;
        }

        // --- public int GetStatus(int timeout) ---
        /// <summary>
        /// Returns the current status of the machine, waiting for the duration of 'timeout' for a response.
        /// </summary>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <returns name="state"> An 'int' that represents the returned status of the machine or '-1' if no response is received in the expected time frame of 'timeout'. </returns>
        public int GetStatus(int timeout)
        {
            int state = -1;

            base.QueueCommand("s");

            ackEvent.Reset();

            if (ackEvent.WaitOne(timeout))
            {
                state = Convert.ToInt32(AckMessage);
            }

            return state;
        }

        // --- public void DriveSleep() ---
        /// <summary>
        /// Sends the drive sleep command to turn off the amp drive.
        /// </summary>
        public void DriveSleep()
        {
            base.QueueCommand("f6");
        }

        // --- public void DriveWake() ---
        /// <summary>
        /// Sends the wake up command to turn on the amp drive.
        /// </summary>
        public void DriveWake()
        {
            base.QueueCommand("f7");
        }

        // --- public void WriteCommand(string command) ---
        /// <summary>
        /// Sends 'command' to the machine to execute.
        /// </summary>
        /// <param name="command"> A 'string' representing the command being sent to the machine to execute. </param>
        public void WriteCommand(string command)
        {
            base.QueueCommand(command);
        }

        // --- public bool UpdateSetting(string settingName, int newValue) ---
        /// <summary>
        /// Changes the setting value of 'settingName' to 'newValue'.
        /// </summary>
        /// <param name="settingName"> A 'string' the represents the name of the setting to change. </param>
        /// <param name="newValue"> A 'double' that represents the new value 'settingName' is being changed to. </param>
        /// <returns name="isUpdated"> A 'bool' that signals whether the setting was successfully changed. </returns>
        public bool UpdateSetting(string settingName, double newValue)
        {
            bool isChanged = false;

            base.ChangeSetting("d" + (settingNames.IndexOf(settingName) + 1) + " " + newValue);

            if (AckMessage == "SetUp")
            {
                isChanged = true;
            }
            else
            {
                isChanged = false;
            }

            return isChanged;
        }

        // --- public bool UpdateSetting(string settingName, string newValue) ---
        /// <summary>
        /// Changes the setting value of 'settingName' to 'newValue'.
        /// </summary>
        /// <param name="settingName"> A 'string' the represents the name of the setting to change. </param>
        /// <param name="newValue"> An 'string' that represents the new value 'settingName' is being changed to. </param>
        /// <returns name="isUpdated"> A 'bool' that signals whether the setting was successfully changed. </returns>
        public bool UpdateSetting(string settingName, string newValue)
        {
            bool isChanged = false;

            base.ChangeSetting("d" + (settingNames.IndexOf(settingName) + 1) + " " + newValue);

            if (AckMessage == "SetUp")
            {
                isChanged = true;
            }
            else
            {
                isChanged = false;
            }

            return isChanged;
        }

        // --- public bool UpdateSetting(int settingIndex, int newValue) ---
        /// <summary>
        /// Changes the setting value at 'settingIndex' to 'newValue'.
        /// </summary>
        /// <param name="settingIndex"> An 'int' the represents the index of the setting to change. </param>
        /// <param name="newValue"> An 'int' that represents the new value the setting is being changed to. </param>
        /// <returns name="isUpdated"> A 'bool' that signals whether the setting was successfully changed. </returns>
        public bool UpdateSetting(int settingIndex, double newValue)
        {
            bool isChanged = false;

            base.ChangeSetting("d" + settingIndex + " " + newValue);

            if (AckMessage == "SetUp")
            {
                isChanged = true;
            }
            else
            {
                isChanged = false;
            }

            return isChanged;
        }

        // --- public bool UpdateSetting(int settingIndex, string newValue) ---
        /// <summary>
        /// Changes the setting value at 'settingIndex' to 'newValue'.
        /// </summary>
        /// <param name="settingIndex"> An 'int' the represents the index of the setting to change. </param>
        /// <param name="newValue"> A 'double' that represents the new value the setting is being changed to. </param>
        /// <returns name="isUpdated"> A 'bool' that signals whether the setting was successfully changed. </returns>
        public bool UpdateSetting(int settingIndex, string newValue)
        {
            bool isChanged = false;

            base.ChangeSetting("d" + settingIndex + " " + newValue);

            if (AckMessage == "SetUp")
            {
                isChanged = true;
            }
            else
            {
                isChanged = false;
            }

            return isChanged;
        }

        // --- public bool DetectToolCycle() ---
        /// <summary>
        /// Listens for the expected sequence of acknowledgments from the machine, making sure that the deadman off signal followed by the deadman on signal
        /// is received by the system, meaning a full tool cycle occurred.
        /// </summary>
        /// <returns name="cycled"> A 'bool' denoting whether a deadman off and on signal was received in sequence, defining a full tool cycle has occurred. </returns>
        public bool DetectToolCycle()
        {
            bool cycled = false;

            deadmanOffEvent.Reset();
            deadmanOffEvent.WaitOne(-1);

            deadmanOnEvent.Reset();
            deadmanOnEvent.WaitOne(-1);

            return cycled;
        }

        // --- public bool DetectToolCycle(int timeout) ---
        /// <summary>
        /// Listens for the expected sequence of acknowledgments from the machine, make sure that the deadman off signal followed by the deadman on signal
        /// us received by the system, meaning a full tool cycle occurred. Each of the waiting events will wait for the duration of 'timeout' for a response.
        /// </summary>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <returns name="cycled"> A 'bool' denoting whether a deadman off and on signal was received in sequence, defining a full tool cycle has occurred. </returns>
        public bool DetectToolCycle(int timeout)
        {
            bool isDmOff = false;
            bool isDmOn = false;
            bool cycled = false;

            deadmanOffEvent.Reset();

            if (deadmanOffEvent.WaitOne(timeout))
            {
                isDmOff = true;
            }

            deadmanOnEvent.Reset();

            if (deadmanOnEvent.WaitOne(timeout))
            {
                isDmOn = true;
            }

            if (isDmOff && isDmOn)
            {
                cycled = true;
            }

            return cycled;
        }

        // --- public bool DetectToolCycle(int timeout1, int timeout 2) ---
        /// <summary>
        /// Listens for the expected sequence of acknowledgments from the machine, make sure that the deadman off signal followed by the deadman on signal
        /// us received by the system, meaning a full tool cycle occurred. The first waiting event will wait for the duration of 'timeout1' for a response,
        /// and the second waiting event will wait for the duration of 'timeout2' for a response.
        /// </summary>
        /// <param name="timeout1"> An 'int' representing the desired timeout value in milliseconds the first event will wait for a response. </param>
        /// <param name="timeout2"> An 'int' representing the desired timeout value in milliseconds the second event will wait for a response. </param>
        /// <returns name="cycled"> A 'bool' denoting whether a deadman off and on signal was received in sequence, defining a full tool cycle has occurred. </returns>
        public bool DetectToolCycle(int timeout1, int timeout2)
        {
            bool isDmOff = false;
            bool isDmOn = false;
            bool cycled = false;

            deadmanOffEvent.Reset();

            if (deadmanOffEvent.WaitOne(timeout1))
            {
                isDmOff = true;
            }

            deadmanOnEvent.Reset();

            if (deadmanOnEvent.WaitOne(timeout2))
            {
                isDmOn = true;
            }

            if (isDmOff && isDmOn)
            {
                cycled = true;
            }

            return cycled;
        }

        // --- public bool DetectToolCycle(ref BacgroundWorker b) ---
        /// <summary>
        /// Listens for the expected sequence of acknowledgments from the machine, make sure that the deadman off signal followed by the deadman on signal
        /// us received by the system, meaning a full tool cycle occurred.
        /// </summary>
        /// <param name="b"> A 'BackgroundWorker' that is running DetectToolCycle() that may signal an impending cancellation. </param>
        /// <returns name="cycled"> A 'bool' denoting whether a deadman off and on signal was received in sequence, defining a full tool cycle has occurred. </returns>
        public bool DetectToolCycle(ref BackgroundWorker b)
        {
            bool isDmOff = false;
            bool isDmOn = false;
            bool cycled = false;

            if (b.CancellationPending)
            {
                return false;
            }

            deadmanOffEvent.Reset();
            deadmanOffEvent.WaitOne(-1);
            isDmOff = true;

            if (b.CancellationPending)
            {
                return false;
            }

            deadmanOnEvent.Reset();
            deadmanOnEvent.WaitOne(-1);
            isDmOn = true;

            if (b.CancellationPending)
            {
                return false;
            }

            if (isDmOff && isDmOn)
            {
                cycled = true;
            }

            return cycled;
        }

        // --- public bool DetectToolCycle(int timeout, ref BackgroundWorker b) ---
        /// <summary>
        /// Listens for the expected sequence of acknowledgments from the machine, make sure that the deadman off signal followed by the deadman on signal
        /// us received by the system, meaning a full tool cycle occurred. Each of the waiting events will wait for the duration of 'timeout' for a response.
        /// </summary>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the function will wait for a response. </param>
        /// <param name="b"> A 'BackgroundWorker' that is running DetectToolCycle() that may signal an impending cancellation. </param>
        /// <returns name="cycled"> A 'bool' denoting whether a deadman off and on signal was received in sequence, defining a full tool cycle has occurred. </returns>
        public bool DetectToolCycle(int timeout, ref BackgroundWorker b)
        {
            bool isDmOff = false;
            bool isDmOn = false;
            bool cycled = false;

            if (b.CancellationPending)
            {
                return false;
            }

            deadmanOffEvent.Reset();
            if (deadmanOffEvent.WaitOne(timeout))
            {
                isDmOff = true;
            }

            if (b.CancellationPending)
            {
                return false;
            }

            deadmanOnEvent.Reset();
            if (deadmanOnEvent.WaitOne(timeout))
            {
                isDmOn = true;
            }

            if (b.CancellationPending)
            {
                return false;
            }

            if (isDmOff && isDmOn)
            {
                cycled = true;
            }

            return cycled;
        }

        // --- public bool DetectToolCycle(int timeout1, int timeout2, ref BackgroundWorker b) ---
        /// <summary>
        /// Listens for the expected sequence of acknowledgments from the machine, make sure that the deadman off signal followed by the deadman on signal
        /// us received by the system, meaning a full tool cycle occurred. The first waiting event will wait for the duration of 'timeout1' for a response,
        /// and the second waiting event will wait for the duration of 'timeout2' for a response.
        /// </summary>
        /// <param name="timeout1"> An 'int' representing the desired timeout value in milliseconds the first event will wait for a response. </param>
        /// <param name="timeout2"> An 'int' representing the desired timeout value in milliseconds the second event will wait for a response. </param>
        /// <param name="b"> A 'BackgroundWorker' that is running DetectToolCycle() that may signal an impending cancellation. </param>
        /// <returns name="cycled"> A 'bool' denoting whether a deadman off and on signal was received in sequence, defining a full tool cycle has occurred. </returns>
        public bool DetectToolCycle(int timeout1, int timeout2, ref BackgroundWorker b)
        {
            bool isDmOff = false;
            bool isDmOn = false;
            bool cycled = false;

            if (b.CancellationPending)
            {
                return false;
            }

            deadmanOffEvent.Reset();
            if (deadmanOffEvent.WaitOne(timeout1))
            {
                isDmOff = true;
            }

            if (b.CancellationPending)
            {
                return false;
            }

            deadmanOnEvent.Reset();
            if (deadmanOnEvent.WaitOne(timeout2))
            {
                isDmOn = true;
            }

            if (b.CancellationPending)
            {
                return false;
            }

            if (isDmOff && isDmOn)
            {
                cycled = true;
            }

            return cycled;
        }

        // --- public bool IO_Connection(bool onOff) ---
        /// <summary>
        /// Turns the desired IO connection on the IO panel on or off depending on 'onOff'.
        /// </summary>
        /// <param name="ioNum"> An 'int' denoting which IO connection, 1 - 12, on the IO panel to change. If 'ioNum' is outside of the range 1 - 12, 'false' is returned. </param>
        /// <param name="onOff"> A 'bool' that signals whether to turn the connection on if 'onOff' is 'true' and off if 'onOff' is 'false'. </param>
        /// <returns name="acknowledged"> A 'bool' denoting whether an acknowledgment was received for the command. </returns>
        public bool IO_Connection(int ioNum, bool onOff)
        {
            bool acknowledged = false;
            string mask = string.Empty;

            switch (ioNum)
            {
                case 1:
                    mask = "0x001";
                    break;
                case 2:
                    mask = "0x002";
                    break;
                case 3:
                    mask = "0x004";
                    break;
                case 4:
                    mask = "0x008";
                    break;
                case 5:
                    mask = "0x010";
                    break;
                case 6:
                    mask = "0x020";
                    break;
                case 7:
                    mask = "0x040";
                    break;
                case 8:
                    mask = "0x080";
                    break;
                case 9:
                    mask = "0x100";
                    break;
                case 10:
                    mask = "0x200";
                    break;
                case 11:
                    mask = "0x400";
                    break;
                case 12:
                    mask = "0x800";
                    break;
                default:
                    return acknowledged;
            }

            if (onOff)
            {
                base.QueueCommand("ww " + "0xFFF " + mask);
            }
            else
            {
                base.QueueCommand("ww " + "0x000 " + mask);
            }

            ackEvent.Reset();
            ackEvent.WaitOne();

            acknowledged = true;

            return acknowledged;
        }

        // --- public bool IO_Connection(bool onOff, int timeout) ---
        /// <summary>
        /// Turns the desired IO connection on the IO panel on or off depending on 'onOff', waiting for the duration of 'timeout' for a response.
        /// </summary>
        /// <param name="ioNum"> An 'int' denoting which IO connection, 1 - 12, on the IO panel to change. If 'ioNum' is outside of the range 1 - 12, 'false' is returned. </param>
        /// <param name="onOff"> A 'bool' that signals whether to turn the connection on if 'onOff' is 'true' and off if 'onOff' is 'false'. </param>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the event will wait for a response. </param>
        /// <returns name="acknowledged"> A 'bool' denoting whether an acknowledgment was received for the command. </returns>
        public bool IO_Connection(int ioNum, bool onOff, int timeout)
        {
            bool acknowledged = false;
            string mask = string.Empty;

            switch (ioNum)
            {
                case 1:
                    mask = "0x001";
                    break;
                case 2:
                    mask = "0x002";
                    break;
                case 3:
                    mask = "0x004";
                    break;
                case 4:
                    mask = "0x008";
                    break;
                case 5:
                    mask = "0x010";
                    break;
                case 6:
                    mask = "0x020";
                    break;
                case 7:
                    mask = "0x040";
                    break;
                case 8:
                    mask = "0x080";
                    break;
                case 9:
                    mask = "0x100";
                    break;
                case 10:
                    mask = "0x200";
                    break;
                case 11:
                    mask = "0x400";
                    break;
                case 12:
                    mask = "0x800";
                    break;
                default:
                    return acknowledged;
            }

            if (onOff)
            {
                base.QueueCommand("ww " + "0xFFF " + mask);
            }
            else
            {
                base.QueueCommand("ww " + "0x000 " + mask);
            }

            ackEvent.Reset();

            if (!ackEvent.WaitOne(timeout))
            {
                acknowledged = false;
            }
            else
            {
                acknowledged = true;
            }

            return acknowledged;
        }

        // --- public List<double> ScanDefectedLength() ---
        /// <summary>
        /// Sends a scan command to the machine and waits until all of the UV detected marks and the material length have been determined and returns them as a 'List'.
        /// </summary>
        /// <returns name="marks"> Returns a 'List' of 'double's that define the position of each defect as well as the length of the material, as the final value at index n-1. </returns>
        public List<double> ScanDefectedLength()
        {
            List<double> marks = new List<double>();

            ackEvent.Reset();

            base.QueueCommand(new byte[] { 0x02, 0x52, 0x00, 0x01, 0x80, 0x00, 0x00, 0x00, 0x0d, 0x0a });

            ackEvent.WaitOne(-1);

            try
            {
                marks = AckMessage.Split(';').ToList().Select(s => double.Parse(s)).ToList();
            }
            catch
            {

                marks.Clear();
            }

            return marks;
        }

        // --- public List<double> ScanDefectedLength(int timeout) ---
        /// <summary>
        /// Sends a scan command to the machine and waits until all of the UV detected marks and the material length have been determined, or the duration of 'timeout', and returns them as a 'List'.
        /// </summary>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the event will wait for a response. </param>
        /// <returns name="marks"> Returns a 'List' of 'double's that define the position of each defect as well as the length of the material, as the final value at index n-1. </returns>
        public List<double> ScanDefectedLength(int timeout)
        {
            List<double> marks = new List<double>();

            ackEvent.Reset();

            base.QueueCommand(new byte[] { 0x02, 0x52, 0x00, 0x01, 0x80, 0x00, 0x00, 0x00, 0x0d, 0x0a });

            ackEvent.WaitOne(timeout);

            try
            {
                marks = AckMessage.Split(';').ToList().Select(s => double.Parse(s)).ToList();
            }
            catch
            {

                marks.Clear();
            }

            return marks;
        }

        // --- public double RandomLengthMeasure() ---
        /// <summary>
        /// Sends a measure command to the machine and waits until the material length has been determined.
        /// NOTE: 'length' values will always be in imperial inches. If metric values are desired, uncomment conversion and modify as needed.
        /// </summary>
        /// <returns name="length"> A 'double' denoting the measured material length. </returns>
        public double RandomLengthMeasure()
        {
            double length = -1;

            measureEvent.Reset();

            base.QueueCommand("tm");

            measureEvent.WaitOne(-1);

            double.TryParse(AckMessage.TrimStart('T', 'M', 'F', ' '), out length);

            //if(want metric)
            //{
            //  length * 25.4;
            //}

            return length;
        }

        // --- public double RandomLengthMeasure(int timeout) ---
        /// <summary>
        /// Sends a measure command to the machine and waits until the material length has been determined or the duration of 'timeout'.
        /// NOTE: 'length' values will always be in imperial inches. If metric values are desired, uncomment conversion and modify as needed.
        /// </summary>
        /// <param name="timeout"> An 'int' representing the desired timeout value in milliseconds the event will wait for a response. </param>
        /// <returns></returns>
        public double RandomLengthMeasure(int timeout)
        {
            double length = -1;

            measureEvent.Reset();

            base.QueueCommand("tm");

            measureEvent.WaitOne(timeout);

            double.TryParse(AckMessage.TrimStart('T', 'M', 'F', ' '), out length);

            //if(want metric)
            //{
            //  length * 25.4;
            //}

            return length;
        }
    }
}
