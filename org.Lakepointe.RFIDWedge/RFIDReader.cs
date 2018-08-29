using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace org.Lakepointe.RFIDWedge.CA
{
    class RFIDReader
    {
        #region Fields
        private static RFIDReader reader = null;
        private ReadMode mode = ReadMode.DEFAULT;
        private SerialPort serialPort = null;
        #endregion

        #region Properties
        public bool EnableLogging { get; set; } = false;

        public ReadMode Mode
        {
            get
            {
                return mode;
            }
            set
            {
                mode = value;
            }
        }

        public string PortAddress { get; set; }
        #endregion

        #region Constructor
        private RFIDReader() { }
        #endregion

        #region Public Methods
        public void Close()
        {
            if ( serialPort != null && serialPort.IsOpen )
            {
                serialPort.Close();
            }
        }

        public void Configure()
        {
            if ( String.IsNullOrWhiteSpace( PortAddress ) )
            {
                PortAddress = ReadSetting( "COM_Port" );
            }

            if ( Mode == ReadMode.DEFAULT )
            {
                var modeValue = ReadSetting( "Mode" );

                if ( String.IsNullOrWhiteSpace( modeValue ) || !Enum.TryParse( modeValue, out mode ) )
                {
                    Mode = ReadMode.DEFAULT;
                }
            }

            serialPort = new SerialPort( PortAddress );
            serialPort.BaudRate = 9600;
            serialPort.Parity = Parity.None;
            serialPort.DataBits = 8;
            serialPort.StopBits = StopBits.One;

            serialPort.DataReceived += SerialPort_DataReceived;
        }

        public bool Open()
        {
            try
            {
                if ( serialPort == null )
                {
                    throw new Exception( "Port Not Configured" );
                }

                serialPort.Open();

                return serialPort.IsOpen;
            }
            catch ( Exception )
            {
                return false;
            }
        }

        #endregion

        #region Private Methods
        private string ConvertToAWID( string data )
        {
            data = data.Substring( 0, 10 );
            var byteArr = Enumerable.Range( 0, data.Length )
                     .Where( x => x % 2 == 0 )
                     .Select( x => Convert.ToByte( data.Substring( x, 2 ), 16 ) )
                     .ToArray();

            List<bool> cardvalue = new List<bool>();

            BitArray bits = new BitArray( byteArr );


            //flip bits in each byte because of endian
            for ( int i = 0; i < byteArr.Length; i++ )
            {
                int start = ( ( i + 1 ) * 8 ) - 1;

                for ( int b = start; b > ( start - 8 ); b-- )
                {
                    cardvalue.Add( bits[b] );
                }
            }

            //exclude bit in position 0 it is a parity bit
            //get facility code (bits in positions 1-8)
            var facLeftToRight = cardvalue.Skip( 1 ).Take( 8 ).ToArray();
            var facBitArrayPrep = new List<bool>();
            //the bits have to be reversed due to endian
            for ( int i = 7; i >= 0; i-- )
            {
                facBitArrayPrep.Add( facLeftToRight[i] );
            }

            //get the card id (bits in positions 9-32)
            var idLeftToRight = cardvalue.Skip( 9 ).Take( 24 ).ToArray();
            var idBitArrayPrep = new List<bool>();
            //have to pad with an extra byte becasue I am only using 24 bits of the required 32
            idBitArrayPrep.AddRange( new bool[] { false, false, false, false, false, false, false, false } );
            //bits in byte have to be reversed because of endian
            for ( int i = 0; i < 3; i++ )
            {
                int start = ( ( i + 1 ) * 8 - 1 );
                for ( int b = start; b > ( start - 8 ); b-- )
                {
                    idBitArrayPrep.Add( idLeftToRight[b] );
                }
            }

            BitArray facBitArray = new BitArray( facBitArrayPrep.ToArray() );
            byte[] facByteArr = new byte[4];
            facBitArray.CopyTo( facByteArr, 0 );

            //get the integer representation of the facility code
            int facCode = BitConverter.ToInt32( facByteArr, 0 );

            BitArray idBitArray = new BitArray( idBitArrayPrep.ToArray() );
            byte[] idByteArr = new byte[4];
            idBitArray.CopyTo( idByteArr, 0 );


            //flip it because of endian
            idByteArr = idByteArr.AsEnumerable().Reverse().ToArray();

            //get the integer id
            int id = BitConverter.ToInt32( idByteArr, 0 );


            //i can FINALLY return the card value!
            return string.Concat( facCode.ToString(), ":", id.ToString() );
        }

        private string ReadSetting( string key )
        {
            return ConfigurationManager.AppSettings.Get( key );
        }

        private void SerialPort_DataReceived( object sender, SerialDataReceivedEventArgs e )
        {
            var port = ( SerialPort ) sender;

            string rawdata = port.ReadLine();
            string processedData = null;

            if ( mode == ReadMode.AWID || mode == ReadMode.DEFAULT )
            {
                processedData = ConvertToAWID( rawdata );

            }
            else
            {
                processedData = rawdata;
            }

            if ( EnableLogging )
            {
                string logData = string.Format( "Raw Data: {0} Processed Data: {1}", rawdata.Replace( "\r", "" ), processedData );
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine( logData );
                Console.ForegroundColor = originalColor;
            }

            // escape special characters
            processedData = Regex.Replace( processedData, "[+^%~()]", "{$0}" );

            // append enter
            processedData = processedData + "~";

            SendKeys.SendWait( processedData );
        }
        #endregion

        #region Static Methods
        static void Main( string[] args )
        {
            reader = new RFIDReader();

            var portArg = args.Where( a => a.StartsWith( @"/port:", StringComparison.InvariantCultureIgnoreCase ) ).FirstOrDefault();
            var modeArg = args.Where( a => a.StartsWith( @"/mode:", StringComparison.InvariantCultureIgnoreCase ) ).FirstOrDefault();
            var logArg = args.Where( a => a.StartsWith( "@/log", StringComparison.InvariantCultureIgnoreCase ) ).FirstOrDefault();

            if ( !String.IsNullOrWhiteSpace( portArg ) )
            {
                portArg = portArg.Remove( 0, 6 );
                reader.PortAddress = portArg.Trim();
            }

            if ( !String.IsNullOrWhiteSpace( modeArg ) )
            {
                modeArg = modeArg.Remove( 0, 6 ).ToUpper();

                ReadMode readMode = ReadMode.DEFAULT;

                if ( Enum.TryParse( modeArg, out readMode ) )
                {
                    reader.Mode = readMode;
                }
            }

            reader.Configure();
            var status = reader.Open();

            if ( status )
            {
                bool isCloseRequested = false;

                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

                while ( !isCloseRequested )
                {
                    Console.Clear();
                    Console.WriteLine( "RFID Reader Active" );
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine( "Press 'C' to close." );
                    Console.ResetColor();

                    var key = Console.ReadKey();

                    if ( key.KeyChar == 'C' || key.KeyChar == 'c' )
                    {
                        isCloseRequested = true;
                    }
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine( "Unable to Open RFID COM Port." );
                Console.WriteLine( "Press any key to close" );
                Console.ReadKey();
            }
        }

        private static void CurrentDomain_ProcessExit( object sender, EventArgs e )
        {
            if ( reader != null )
            {
                reader.Close();
            }
        }
        #endregion
    }
}
