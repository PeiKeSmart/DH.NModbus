namespace NewLife.IoT.Models;

/// <summary>Modbus设备标识信息（FC43 MEI传输，ReadDevId响应数据）</summary>
public class ModbusDeviceInfo
{
    #region 属性
    /// <summary>厂商名称（ObjectId=0x00）</summary>
    public String VendorName { get; set; } = "NewLife";

    /// <summary>产品代码（ObjectId=0x01）</summary>
    public String ProductCode { get; set; } = "Modbus";

    /// <summary>版本修订号（ObjectId=0x02）</summary>
    public String Revision { get; set; } = "1.0";
    #endregion

    #region 方法
    /// <summary>获取所有对象列表（ObjectId, Value）</summary>
    /// <returns>对象ID与字符串值的元组列表</returns>
    public IList<(Byte Id, String Value)> GetObjects()
    {
        return
        [
            (0x00, VendorName ?? String.Empty),
            (0x01, ProductCode ?? String.Empty),
            (0x02, Revision ?? String.Empty),
        ];
    }
    #endregion
}
