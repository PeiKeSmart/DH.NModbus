using System;
using System.ComponentModel;
using System.Linq;
using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NewLife.Serial.Protocols;
using Xunit;

namespace XUnitTest.CrossCompatibility;

/// <summary>ModbusRTU 竞品交叉兼容测试（字节级帧验证）</summary>
/// <remarks>
/// 交叉验证模式：
/// - N2-RTU帧：手动构造 RTU 帧 → ModbusRtuSlave.ProcessRequest() 解析 → 验证响应帧
///
/// 覆盖 CRC 编码/解码、帧定界、异常响应、广播模式等边界场景。
/// 不依赖网络连接，所有测试为纯字节级验证。
/// </remarks>
public class ModbusRtuCrossCompatibilityTests
{
    #region N2 模式：RTU 帧编码/解码交叉验证（字节级）

    /// <summary>
    /// RTU 帧字节级交叉验证：
    /// 手动构造标准 RTU 帧 → ModbusRtuSlave.ProcessRequest() 解析 → 验证响应帧格式与内容
    /// </summary>
    public class N2RtuFrameLevelTests
    {
        private readonly ModbusRtuSlave _slave;

        public N2RtuFrameLevelTests()
        {
            _slave = new ModbusRtuSlave
            {
                Host = 1,
                Registers = Enumerable.Range(0, 10)
                    .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(1000 + i) })
                    .ToList(),
                Coils = Enumerable.Range(0, 16)
                    .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                    .ToList(),
            };
        }

        [Fact]
        [DisplayName("N2-RTU帧 FC03 ReadHoldingRegisters 手动构造帧→解析响应帧结构")]
        public void N2_RtuFrame_FC03_ReadHoldingRegisters_VerifyStructure()
        {
            // 手动构造 RTU 请求: [地址=01] [FC=03] [起始=00 00] [数量=00 02] [CRC]
            var addr = new Byte[] { 0x00, 0x00 };
            var count = new Byte[] { 0x00, 0x02 };
            var frame = new Byte[] { 0x01, 0x03, addr[0], addr[1], count[0], count[1], 0, 0 };
            var crc = ModbusHelper.Crc(frame, 0, 6);
            frame[6] = (Byte)(crc & 0xFF);
            frame[7] = (Byte)(crc >> 8);

            var response = _slave.ProcessRequest(frame);

            Assert.NotNull(response);
            // 响应: [地址=01] [FC=03] [字节数=04] [值1H 值1L] [值2H 值2L] [CRC]
            Assert.True(response!.Length >= 5);
            Assert.Equal(0x01, response[0]);
            Assert.Equal(0x03, response[1]);
            Assert.Equal(0x04, response[2]); // 字节数 = 2×2 = 4
        }

        [Fact]
        [DisplayName("N2-RTU帧 FC01 ReadCoils 手动构造帧→解析响应帧结构")]
        public void N2_RtuFrame_FC01_ReadCoils_VerifyStructure()
        {
            var frame = new Byte[] { 0x01, 0x01, 0x00, 0x00, 0x00, 0x08, 0, 0 };
            var crc = ModbusHelper.Crc(frame, 0, 6);
            frame[6] = (Byte)(crc & 0xFF);
            frame[7] = (Byte)(crc >> 8);

            var response = _slave.ProcessRequest(frame);

            Assert.NotNull(response);
            Assert.True(response!.Length >= 4);
            Assert.Equal(0x01, response[0]);
            Assert.Equal(0x01, response[1]);
            Assert.Equal(0x01, response[2]); // 字节数 = ceil(8/8) = 1
        }

        [Fact]
        [DisplayName("N2-RTU帧 FC06 WriteSingleRegister 手动构造帧→回显验证+数据更新")]
        public void N2_RtuFrame_FC06_WriteSingleRegister_EchoAndUpdate()
        {
            var frame = new Byte[] { 0x01, 0x06, 0x00, 0x03, 0xAB, 0xCD, 0, 0 };
            var crc = ModbusHelper.Crc(frame, 0, 6);
            frame[6] = (Byte)(crc & 0xFF);
            frame[7] = (Byte)(crc >> 8);

            var response = _slave.ProcessRequest(frame);

            Assert.NotNull(response);
            Assert.True(response!.Length >= 6);
            Assert.Equal(0x01, response[0]);
            Assert.Equal(0x06, response[1]);
            // 回显写入值
            Assert.Equal(0xAB, response[4]);
            Assert.Equal(0xCD, response[5]);

            // 验证从机侧寄存器已更新
            var reg = _slave.Registers.First(r => r.Address == 3);
            Assert.Equal(0xABCD, reg.Value);
        }

        [Fact]
        [DisplayName("N2-RTU帧 FC05 WriteSingleCoil ON 帧结构+数据更新验证")]
        public void N2_RtuFrame_FC05_WriteSingleCoil_On_VerifyUpdate()
        {
            var frame = new Byte[] { 0x01, 0x05, 0x00, 0x00, 0xFF, 0x00, 0, 0 };
            var crc = ModbusHelper.Crc(frame, 0, 6);
            frame[6] = (Byte)(crc & 0xFF);
            frame[7] = (Byte)(crc >> 8);

            var response = _slave.ProcessRequest(frame);

            Assert.NotNull(response);
            Assert.Equal(0x01, response![0]);
            Assert.Equal(0x05, response[1]);

            var coil = _slave.Coils.First(c => c.Address == 0);
            Assert.Equal(1, coil.Value);
        }

        [Fact]
        [DisplayName("N2-RTU帧 异常响应 无效功能码0x42返回EC01+0xC2帧头")]
        public void N2_RtuFrame_IllegalFunctionCode_ReturnsEC01()
        {
            var frame = new Byte[] { 0x01, 0x42, 0x00, 0x00, 0x00, 0x01, 0, 0 };
            var crc = ModbusHelper.Crc(frame, 0, 6);
            frame[6] = (Byte)(crc & 0xFF);
            frame[7] = (Byte)(crc >> 8);

            var response = _slave.ProcessRequest(frame);

            Assert.NotNull(response);
            Assert.Equal(0x01, response![0]);
            Assert.Equal(0xC2, response[1]); // 0x42 | 0x80 = 异常标志
            Assert.Equal(0x01, response[2]); // EC01
        }

        [Fact]
        [DisplayName("N2-RTU帧 CRC校验错误 返回null（静默丢弃）")]
        public void N2_RtuFrame_InvalidCrc_ReturnsNull()
        {
            var frame = new Byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01, 0xFF, 0xFF };
            var response = _slave.ProcessRequest(frame);
            Assert.Null(response);
        }

        [Fact]
        [DisplayName("N2-RTU帧 站号不匹配（请求站号2≠从机站号1）返回null")]
        public void N2_RtuFrame_WrongHost_ReturnsNull()
        {
            var frame = new Byte[] { 0x02, 0x03, 0x00, 0x00, 0x00, 0x02, 0, 0 };
            var crc = ModbusHelper.Crc(frame, 0, 6);
            frame[6] = (Byte)(crc & 0xFF);
            frame[7] = (Byte)(crc >> 8);

            var response = _slave.ProcessRequest(frame);
            Assert.Null(response);
        }

        [Fact]
        [DisplayName("N2-RTU帧 广播地址0写单寄存器 执行写入但不返回响应")]
        public void N2_RtuFrame_BroadcastAddress_WriteNoResponse()
        {
            var frame = new Byte[] { 0x00, 0x06, 0x00, 0x01, 0x12, 0x34, 0, 0 };
            var crc = ModbusHelper.Crc(frame, 0, 6);
            frame[6] = (Byte)(crc & 0xFF);
            frame[7] = (Byte)(crc >> 8);

            var response = _slave.ProcessRequest(frame);

            // 广播模式不返回响应
            Assert.Null(response);

            // 但寄存器应被写入
            var reg = _slave.Registers.First(r => r.Address == 1);
            Assert.Equal(0x1234, reg.Value);
        }
    }

    #endregion
}
