﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewLife.IoT.Drivers;
using NewLife.IoT.Protocols;
using NewLife.Serial.Drivers;
using NewLife.Serial.Protocols;
using Xunit;

namespace XUnitTest
{
    public class ModbusRtuDriverTests
    {
        [Fact]
        public void Test1()
        {
            var driver = new ModbusRtuDriver();

            var p = new ModbusRtuParameter
            {
                Host = 3,
                ReadCode = FunctionCodes.ReadRegister,
                WriteCode = FunctionCodes.WriteRegister,
                PortName = "COM1",
            };

            var node = driver.Open(null, p);

            var node2 = node as ModbusNode;
            Assert.NotNull(node2);

            Assert.Equal(p.Host, node2.Host);
            Assert.Equal(p.ReadCode, node2.ReadCode);
            Assert.Equal(p.WriteCode, node2.WriteCode);
            Assert.Null(node2.Device);

            var modbus = driver.Modbus as ModbusRtu;
            Assert.NotNull(modbus);
            Assert.Equal(p.PortName, modbus.PortName);
            Assert.Equal(9600, modbus.Baudrate);
        }
    }
}