using System;
using System.IO.Ports;
using System.Text;

namespace SortingMachineDesktop
{
    public class ScannerClient
    {
        private readonly object _lock = new object();
        private SerialPort _port;
        private string _portName;
        private int _baudRate;
        private Parity _parity;
        private StringBuilder _buffer;

        public string LastBarcode { get; private set; }
        public DateTime LastBarcodeTime { get; private set; }
        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _port != null && _port.IsOpen;
                }
            }
        }

        public bool EnsureOpen(string portName, int baudRate, Parity parity)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(portName))
                {
                    return false;
                }

                if (_port != null && _port.IsOpen && _portName == portName && _baudRate == baudRate && _parity == parity)
                {
                    return true;
                }

                Close();

                try
                {
                    _buffer = new StringBuilder();
                    _port = new SerialPort(portName, baudRate, parity, 8, StopBits.One);
                    _port.NewLine = "\r\n";
                    _port.DataReceived += OnDataReceived;
                    _port.ReadTimeout = 200;
                    _port.WriteTimeout = 200;
                    _port.Open();
                    _portName = portName;
                    _baudRate = baudRate;
                    _parity = parity;
                    return true;
                }
                catch
                {
                    Close();
                    return false;
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                if (_port != null)
                {
                    try
                    {
                        _port.DataReceived -= OnDataReceived;
                        _port.Close();
                    }
                    catch
                    {
                        // ignore
                    }
                }
                _port = null;
                _portName = null;
                _baudRate = 0;
                _parity = Parity.None;
            }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var data = _port.ReadExisting();
                if (string.IsNullOrEmpty(data))
                {
                    return;
                }

                lock (_lock)
                {
                    _buffer.Append(data);
                    var text = _buffer.ToString();
                    var parts = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    if (parts.Length > 1)
                    {
                        for (var i = 0; i < parts.Length - 1; i++)
                        {
                            var line = parts[i].Trim();
                            if (!string.IsNullOrEmpty(line))
                            {
                                LastBarcode = line;
                                LastBarcodeTime = DateTime.Now;
                            }
                        }
                        _buffer.Clear();
                        _buffer.Append(parts[parts.Length - 1]);
                    }
                    else
                    {
                        if (text.Length > 256)
                        {
                            _buffer.Clear();
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
