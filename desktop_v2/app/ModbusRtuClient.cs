using System;
using System.IO.Ports;

namespace SortingMachineDesktop
{
    public class ModbusRtuClient
    {
        private readonly object _lock = new object();
        private SerialPort _port;
        private string _portName;
        private int _baudRate;

        public bool IsConnected
        {
            get { return _port != null && _port.IsOpen; }
        }

        public bool TryReadHoldingRegisters(string portName, int baudRate, byte slaveId, ushort start, ushort count, out ushort[] registers)
        {
            registers = null;
            lock (_lock)
            {
                if (!EnsurePort(portName, baudRate))
                {
                    return false;
                }

                var frame = BuildReadFrame(slaveId, start, count);
                try
                {
                    _port.DiscardInBuffer();
                    _port.Write(frame, 0, frame.Length);

                    var expected = 5 + count * 2;
                    var response = ReadExact(expected);
                    if (response == null || response.Length != expected)
                    {
                        return false;
                    }

                    if (!CheckCrc(response))
                    {
                        return false;
                    }

                    if (response[0] != slaveId || response[1] != 0x03)
                    {
                        return false;
                    }

                    var byteCount = response[2];
                    if (byteCount != count * 2)
                    {
                        return false;
                    }

                    var data = new ushort[count];
                    for (var i = 0; i < count; i++)
                    {
                        var hi = response[3 + i * 2];
                        var lo = response[4 + i * 2];
                        data[i] = (ushort)((hi << 8) | lo);
                    }

                    registers = data;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool TryWriteHoldingRegisters(string portName, int baudRate, byte slaveId, ushort start, ushort[] values)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            lock (_lock)
            {
                if (!EnsurePort(portName, baudRate))
                {
                    return false;
                }

                var frame = BuildWriteMultipleFrame(slaveId, start, values);
                try
                {
                    _port.DiscardInBuffer();
                    _port.Write(frame, 0, frame.Length);

                    var response = ReadExact(8);
                    if (response == null || response.Length != 8)
                    {
                        return false;
                    }

                    if (!CheckCrc(response))
                    {
                        return false;
                    }

                    if (response[0] != slaveId || response[1] != 0x10)
                    {
                        return false;
                    }

                    var echoedStart = (ushort)((response[2] << 8) | response[3]);
                    var echoedCount = (ushort)((response[4] << 8) | response[5]);
                    return echoedStart == start && echoedCount == values.Length;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool TryWriteSingleHoldingRegister(string portName, int baudRate, byte slaveId, ushort address, ushort value)
        {
            lock (_lock)
            {
                if (!EnsurePort(portName, baudRate))
                {
                    return false;
                }

                var frame = BuildWriteSingleHoldingFrame(slaveId, address, value);
                try
                {
                    _port.DiscardInBuffer();
                    _port.Write(frame, 0, frame.Length);

                    var response = ReadExact(8);
                    if (response == null || response.Length != 8)
                    {
                        return false;
                    }

                    if (!CheckCrc(response))
                    {
                        return false;
                    }

                    if (response[0] != slaveId || response[1] != 0x06)
                    {
                        return false;
                    }

                    var echoedAddress = (ushort)((response[2] << 8) | response[3]);
                    var echoedValue = (ushort)((response[4] << 8) | response[5]);
                    return echoedAddress == address && echoedValue == value;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool TryWriteSingleCoil(string portName, int baudRate, byte slaveId, ushort address, bool value)
        {
            lock (_lock)
            {
                if (!EnsurePort(portName, baudRate))
                {
                    return false;
                }

                var frame = BuildWriteSingleCoilFrame(slaveId, address, value);
                try
                {
                    _port.DiscardInBuffer();
                    _port.Write(frame, 0, frame.Length);

                    var response = ReadExact(8);
                    if (response == null || response.Length != 8)
                    {
                        return false;
                    }

                    if (!CheckCrc(response))
                    {
                        return false;
                    }

                    if (response[0] != slaveId || response[1] != 0x05)
                    {
                        return false;
                    }

                    var echoedAddress = (ushort)((response[2] << 8) | response[3]);
                    var echoedValue = (ushort)((response[4] << 8) | response[5]);
                    var expectedValue = value ? (ushort)0xFF00 : (ushort)0x0000;
                    return echoedAddress == address && echoedValue == expectedValue;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static float RegistersToFloat(ushort reg0, ushort reg1, bool swapWords)
        {
            ushort high = swapWords ? reg1 : reg0;
            ushort low = swapWords ? reg0 : reg1;
            var bytes = new byte[4];
            bytes[0] = (byte)(high >> 8);
            bytes[1] = (byte)(high & 0xFF);
            bytes[2] = (byte)(low >> 8);
            bytes[3] = (byte)(low & 0xFF);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToSingle(bytes, 0);
        }

        public static uint RegistersToUInt32(ushort reg0, ushort reg1, bool swapWords)
        {
            ushort high = swapWords ? reg1 : reg0;
            ushort low = swapWords ? reg0 : reg1;
            return ((uint)high << 16) | (uint)low;
        }

        public static ushort[] FloatToRegisters(float value, bool swapWords)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            var high = (ushort)((bytes[0] << 8) | bytes[1]);
            var low = (ushort)((bytes[2] << 8) | bytes[3]);
            if (swapWords)
            {
                return new[] { low, high };
            }

            return new[] { high, low };
        }

        public void Close()
        {
            try
            {
                if (_port != null)
                {
                    _port.Close();
                    _port = null;
                }
            }
            catch
            {
                // liberation best-effort: le port doit etre rendu au systeme a la fermeture.
            }
        }

        private bool EnsurePort(string portName, int baudRate)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                return false;
            }

            if (_port != null && _port.IsOpen && _portName == portName && _baudRate == baudRate)
            {
                return true;
            }

            try
            {
                if (_port != null)
                {
                    _port.Close();
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                _port.ReadTimeout = 500;
                _port.WriteTimeout = 500;
                _port.Open();
                _portName = portName;
                _baudRate = baudRate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] BuildReadFrame(byte slaveId, ushort start, ushort count)
        {
            var frame = new byte[8];
            frame[0] = slaveId;
            frame[1] = 0x03;
            frame[2] = (byte)(start >> 8);
            frame[3] = (byte)(start & 0xFF);
            frame[4] = (byte)(count >> 8);
            frame[5] = (byte)(count & 0xFF);
            var crc = ComputeCrc(frame, 0, 6);
            frame[6] = (byte)(crc & 0xFF);
            frame[7] = (byte)(crc >> 8);
            return frame;
        }

        private static byte[] BuildWriteSingleHoldingFrame(byte slaveId, ushort address, ushort value)
        {
            var frame = new byte[8];
            frame[0] = slaveId;
            frame[1] = 0x06;
            frame[2] = (byte)(address >> 8);
            frame[3] = (byte)(address & 0xFF);
            frame[4] = (byte)(value >> 8);
            frame[5] = (byte)(value & 0xFF);
            var crc = ComputeCrc(frame, 0, 6);
            frame[6] = (byte)(crc & 0xFF);
            frame[7] = (byte)(crc >> 8);
            return frame;
        }

        private static byte[] BuildWriteMultipleFrame(byte slaveId, ushort start, ushort[] values)
        {
            var frame = new byte[9 + values.Length * 2];
            frame[0] = slaveId;
            frame[1] = 0x10;
            frame[2] = (byte)(start >> 8);
            frame[3] = (byte)(start & 0xFF);
            frame[4] = (byte)(values.Length >> 8);
            frame[5] = (byte)(values.Length & 0xFF);
            frame[6] = (byte)(values.Length * 2);

            for (var i = 0; i < values.Length; i++)
            {
                frame[7 + i * 2] = (byte)(values[i] >> 8);
                frame[8 + i * 2] = (byte)(values[i] & 0xFF);
            }

            var crc = ComputeCrc(frame, 0, frame.Length - 2);
            frame[frame.Length - 2] = (byte)(crc & 0xFF);
            frame[frame.Length - 1] = (byte)(crc >> 8);
            return frame;
        }

        private static byte[] BuildWriteSingleCoilFrame(byte slaveId, ushort address, bool value)
        {
            var frame = new byte[8];
            var coilValue = value ? (ushort)0xFF00 : (ushort)0x0000;
            frame[0] = slaveId;
            frame[1] = 0x05;
            frame[2] = (byte)(address >> 8);
            frame[3] = (byte)(address & 0xFF);
            frame[4] = (byte)(coilValue >> 8);
            frame[5] = (byte)(coilValue & 0xFF);
            var crc = ComputeCrc(frame, 0, 6);
            frame[6] = (byte)(crc & 0xFF);
            frame[7] = (byte)(crc >> 8);
            return frame;
        }

        private byte[] ReadExact(int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            var deadline = DateTime.Now.AddMilliseconds(800);
            while (offset < count && DateTime.Now < deadline)
            {
                try
                {
                    var read = _port.Read(buffer, offset, count - offset);
                    if (read > 0)
                    {
                        offset += read;
                    }
                }
                catch (TimeoutException)
                {
                    break;
                }
            }
            if (offset != count)
            {
                return null;
            }
            return buffer;
        }

        private static bool CheckCrc(byte[] data)
        {
            if (data.Length < 3)
            {
                return false;
            }
            var crc = ComputeCrc(data, 0, data.Length - 2);
            var lo = (byte)(crc & 0xFF);
            var hi = (byte)(crc >> 8);
            return data[data.Length - 2] == lo && data[data.Length - 1] == hi;
        }

        private static ushort ComputeCrc(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            for (var i = 0; i < length; i++)
            {
                crc ^= data[offset + i];
                for (var bit = 0; bit < 8; bit++)
                {
                    var lsb = (crc & 0x0001) != 0;
                    crc >>= 1;
                    if (lsb)
                    {
                        crc ^= 0xA001;
                    }
                }
            }
            return crc;
        }
    }
}
