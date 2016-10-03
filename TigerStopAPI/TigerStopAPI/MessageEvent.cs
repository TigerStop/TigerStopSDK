using System;

namespace TigerStopAPI
{
    /// <summary>
    /// A simpler, more manageable, class of event arguments that is used to pass data between events and event subscribers.
    /// </summary>
    class MessageEvent : EventArgs
    {
        //  =  =  =  FIELDS  =  =  =
        private string message = string.Empty;
        private DateTime time = DateTime.MinValue;
        private double value = double.NaN;

        //  =  =  =  GETTERS/SETTERS  =  =  =
        public string Message
        {
            get
            {
                return message;
            }
        }

        public DateTime Time
        {
            get
            {
                return time;
            }
        }

        public double Value
        {
            get
            {
                return value;
            }
        }

        //  =  =  =  CONSTRUCTORS  =  =  =
        public MessageEvent(string newMessage)
        {
            message = newMessage;
        }

        public MessageEvent(DateTime newTime)
        {
            time = newTime;
        }

        public MessageEvent(double newValue)
        {
            value = newValue;
        }
    }
}
