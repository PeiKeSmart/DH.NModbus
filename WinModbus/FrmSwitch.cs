using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using NewLife.Log;

namespace WinModbus;

public partial class FrmSwitch : Form
{
    private Byte _host = 1;
    private Modbus _modbus;
    private ILog _log;

    public Modbus Modbus { get => _modbus; set => _modbus = value; }

    public Byte Host { get => _host; set => _host = value; }

    public FrmSwitch() => InitializeComponent();

    private void Form1_Load(Object sender, EventArgs e)
    {
    }

    private async void btnOpen1_Click(Object sender, EventArgs e)
    {
        var btn = sender as Button;
        var addr = btn.Tag.ToInt() - 1;
        var delay = (Int32)numDelay.Value;

        if (delay > 0)
            await _modbus.WriteRegistersAsync(_host, (UInt16)(0x0003 + addr * 5), new UInt16[] { 0x0004, (UInt16)(delay / 100) });
        else
            await _modbus.WriteCoilAsync(_host, (UInt16)addr, 0xFF00);
    }

    private async void btnClose1_Click(Object sender, EventArgs e)
    {
        var btn = sender as Button;
        var addr = btn.Tag.ToInt() - 1;
        var delay = (Int32)numDelay.Value;

        if (delay > 0)
            await _modbus.WriteRegistersAsync(_host, (UInt16)(0x0003 + addr * 5), new UInt16[] { 0x0002, (UInt16)(delay / 100) });
        else
            await _modbus.WriteCoilAsync(_host, (UInt16)addr, 0);
    }

    private async void btnOpenAll_Click(Object sender, EventArgs e)
    {
        await _modbus.WriteCoilsAsync(_host, 0, new UInt16[] { 0xFF00, 0xFF00, 0xFF00, 0xFF00, 0xFF00, 0xFF00, 0xFF00, 0xFF00 });
    }

    private async void btnCloseAll_Click(Object sender, EventArgs e)
    {
        await _modbus.WriteCoilsAsync(_host, 0, new UInt16[] { 0, 0, 0, 0, 0, 0, 0, 0 });
    }

    private async void btnReadAll_Click(Object sender, EventArgs e)
    {
        await _modbus.ReadCoilAsync(_host, 0, 8);
    }

    private async void btnReadAddr_Click(Object sender, EventArgs e)
    {
        var rs = await _modbus.ReadRegisterAsync(_host, 0, 1);
        if (rs == null || rs.Length == 0) return;

        numAddr.Value = rs[0];
    }

    private async void btnWriteAddr_Click(Object sender, EventArgs e)
    {
        var addr = (UInt16)numAddr.Value;

        await _modbus.WriteRegistersAsync(_host, 0, [addr]);
    }

    private async void btnReadIn_Click(Object sender, EventArgs e)
    {
        await _modbus.ReadDiscreteAsync(_host, 0, 8);
    }
}