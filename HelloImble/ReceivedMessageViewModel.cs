using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelloImble
{
    public class ReceivedMessageViewModel
    {
        public byte[] Data { get; }
        public DateTimeOffset Timestamp { get; }
        public string DecodedString { get; }
        public string RawString { get; }

        public ReceivedMessageViewModel(byte[] data, DateTimeOffset timestamp)
        {
            this.Data = data;
            this.Timestamp = timestamp;

            this.RawString = string.Join(",", data.Select(item => item.ToString("X02")));

            var decoded = "";
            try
            {
                decoded = Encoding.UTF8.GetString(this.Data);
            }
            catch (Exception)
            {
            }
            this.DecodedString = decoded;
        }
    }
}
