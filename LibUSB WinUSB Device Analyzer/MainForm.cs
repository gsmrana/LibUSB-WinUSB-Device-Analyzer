using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using LibUsbDotNet;
using LibUsbDotNet.Main;
using LibUsbDotNet.WinUsb;
using LibUsbDotNet.DeviceNotify;
using System.Diagnostics;

namespace LibUSB_WinUSB_Device_Analyzer
{
    public partial class MainForm : Form
    {
        #region Data

        bool _ep3InterupEnable = false;
        UsbDevice _device;
        UsbEndpointReader _epReader;
        UsbRegDeviceList _devices = new UsbRegDeviceList();
        readonly IDeviceNotifier _usbDeviceNotifier = DeviceNotifier.OpenDeviceNotifier();
        readonly string _astrics = "* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *";

        #endregion

        #region ctor

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            rtbEventLog.Font = new Font("Consolas", 9);
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            try
            {
                UsbDevice.UsbErrorEvent += UsbDevice_UsbErrorEvent;
                _usbDeviceNotifier.OnDeviceNotify += UsbDeviceNotifier_OnDeviceNotify;

                UpdateDeviceList();

                AppendEventLog(_astrics);
                AppendEventLog("OSVersion: " + UsbDevice.OSVersion);
                AppendEventLog("HasWinUsbDriver: " + UsbDevice.HasWinUsbDriver);
                AppendEventLog("LibUsb device count: " + UsbDevice.AllLibUsbDevices.Count);
                AppendEventLog("WinUsb device count: " + UsbDevice.AllWinUsbDevices.Count);
                AppendEventLog("LibUsb/WinUsb Device count: " + UsbDevice.AllDevices.Count);
                AppendEventLog(_astrics);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                DisconnectUsbDevice();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion

        #region Internal Methods

        private void AppendEventLog(string str, Color? color = null, bool appendNewLine = true)
        {
            var clr = color ?? Color.Blue;
            if (appendNewLine) str += Environment.NewLine;
            Invoke(new MethodInvoker(() =>
            {
                rtbEventLog.SelectionStart = rtbEventLog.TextLength;
                rtbEventLog.SelectionLength = 0;
                rtbEventLog.SelectionColor = clr;
                rtbEventLog.AppendText(str);
                if (!rtbEventLog.Focused) rtbEventLog.ScrollToCaret();
            }));
        }

        private void UpdateDeviceList()
        {
            _devices = UsbDevice.AllDevices;
            var count = 0;
            dataGridView1.SelectionChanged -= DataGridView1_SelectionChanged;
            dataGridView1.Rows.Clear();
            foreach (UsbRegistry dev in _devices)
            {
                count++;
                var devinfo = string.Format("VID_{0:X4} PID_{1:X4} REV_{2:X4}", dev.Vid, dev.Pid, dev.Rev);
                var mfg = dev.DeviceProperties.ContainsKey("Mfg") ? dev.DeviceProperties["Mfg"].ToString() : "";
                var driverType = dev is WinUsbRegistry ? "WinUsb" : "LibUsb";
                string[] row = { count.ToString(), devinfo, dev.Name, mfg, driverType, dev.SymbolicName };
                dataGridView1.Rows.Add(row);
            }
            dataGridView1.SelectionChanged += DataGridView1_SelectionChanged;
            DataGridView1_SelectionChanged(this, null);
        }

        private void PopupException(string message, string caption = "Exception")
        {
            Invoke(new Action(() =>
            {
                MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        }

        #endregion

        #region USB Helper Methods

        void ConnectUsbDevice(int vid, int pid)
        {
            // find using vid pid 
            var devInfo = new UsbDeviceFinder(vid, pid);
            _device = UsbDevice.OpenUsbDevice(devInfo);

            //open from local cache
            //_devices[dataGridView1.SelectedRows[0].Index].Open(out _device);

            if (_device == null) throw new Exception("Could not open the device with specified VID PID!");

            if (_device is IUsbDevice libUsbDevice)
            {
                libUsbDevice.SetConfiguration(1);
                libUsbDevice.ClaimInterface(0);
            }
        }

        void DisconnectUsbDevice()
        {
            if (_device != null)
            {
                if (_device.IsOpen)
                {
                    if (_device is IUsbDevice libUsbDevice)
                    {
                        libUsbDevice.ReleaseInterface(0);
                    }
                    _device.Close();
                }
            }
            _device = null;
            UsbDevice.Exit();
        }

        bool USB_ControlTransfer(byte request, int value = 0)
        {
            byte reqType = 0;
            reqType |= (byte)UsbRequestType.TypeVendor;
            reqType |= (byte)UsbRequestRecipient.RecipDevice;
            reqType |= (byte)UsbEndpointDirection.EndpointOut;
            var packet = new UsbSetupPacket(reqType, request, (short)value, 0, 0);

            object buf = null;
            return _device.ControlTransfer(ref packet, buf, 0, out _);
        }

        bool USB_ControlTransWrite(byte request, byte[] buf)
        {
            byte reqType = 0;
            reqType |= (byte)UsbRequestType.TypeVendor;
            reqType |= (byte)UsbRequestRecipient.RecipDevice;
            reqType |= (byte)UsbEndpointDirection.EndpointOut;
            var packet = new UsbSetupPacket(reqType, request, 0, 0, (short)buf.Length);
            return _device.ControlTransfer(ref packet, buf, buf.Length, out _);
        }

        bool USB_ControlTransRead(byte request, ref byte[] buf)
        {
            byte reqType = 0;
            reqType |= (byte)UsbRequestType.TypeVendor;
            reqType |= (byte)UsbRequestRecipient.RecipDevice;
            reqType |= (byte)UsbEndpointDirection.EndpointIn;
            var packet = new UsbSetupPacket(reqType, request, 0, 0, 0);
            return _device.ControlTransfer(ref packet, buf, buf.Length, out _);
        }

        #endregion

        #region USB Events

        void EndpointReader_DataReceived(object sender, EndpointDataEventArgs e)
        {
            AppendEventLog("Ep03 DataReceived: byte[0] --> " + e.Buffer[0] + "\r\nData: " + Encoding.ASCII.GetString(e.Buffer, 0, 256));
        }

        void UsbDeviceNotifier_OnDeviceNotify(object sender, DeviceNotifyEventArgs e)
        {
            AppendEventLog(string.Format("DeviceNotifyEvent --> \n{0} \n{1} \n{2}", e.EventType, e.DeviceType, e.Object));
            UpdateDeviceList();
        }

        void UsbDevice_UsbErrorEvent(object sender, UsbError e)
        {
            AppendEventLog("UsbErrorEvent: " + e);
        }

        #endregion

        #region MenuStrip Events

        private void NewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Application.ExecutablePath);
        }

        private void OpenToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void SaveToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutToolStripButton_Click(sender, e);
        }

        #endregion

        #region Toolstrip Events

        private void ToolStripButtonOpen_Click(object sender, EventArgs e)
        {
            try
            {
                var str = tsTextBoxVidPid.Text.Split(':');
                var vid = Convert.ToInt32(str[0], 16);
                var pid = Convert.ToInt32(str[1], 16);

                DisconnectUsbDevice();
                ConnectUsbDevice(vid, pid);
                AppendEventLog(_astrics);
                AppendEventLog("Device Detais: \n");
                AppendEventLog(_device.Info.ToString());
                AppendEventLog("DriverMode: " + _device.DriverMode);
                AppendEventLog("Configs Count: " + _device.Configs.Count);
                AppendEventLog("Endpoint Count: " + _device.ActiveEndpoints.Count);

                if (_device.ActiveEndpoints.Count > 0) AppendEventLog("\nEndpoint Details: ");
                foreach (var ep in _device.ActiveEndpoints)
                {
                    AppendEventLog("Endpoint Type: " + ep.Type);
                    if (ep.EndpointInfo != null) AppendEventLog("Endpoint Info: " + ep.EndpointInfo);
                }
                AppendEventLog(_astrics);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ToolStripButtonClear_Click(object sender, EventArgs e)
        {
            rtbEventLog.Clear();
        }

        private void AboutToolStripButton_Click(object sender, EventArgs e)
        {
            try
            {
                var aboutbox = new AboutBox();
                aboutbox.ShowDialog();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion

        #region Form Events

        private void ButtonSendValue_Click(object sender, EventArgs e)
        {
            try
            {
                var result = false;
                if (textBoxValue.Text == "")
                {
                    var request = byte.Parse(textBoxRequest.Text);
                    result = USB_ControlTransfer(request);
                }
                else
                {
                    var request = byte.Parse(textBoxRequest.Text);
                    var value = int.Parse(textBoxValue.Text);
                    result = USB_ControlTransfer(request, value);
                }

                AppendEventLog("ControlTransfer: " + result);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ButtonControlTransWrite_Click(object sender, EventArgs e)
        {
            try
            {
                var request = byte.Parse(textBoxRequest.Text);
                var data = Encoding.ASCII.GetBytes(textBoxData.Text);
                var result = USB_ControlTransWrite(request, data);
                AppendEventLog("ControlTransfer: " + result);
                if (result)
                {
                    AppendEventLog("Sent To Device: " + Encoding.ASCII.GetString(data));
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ButtonControlTransRead_Click(object sender, EventArgs e)
        {
            try
            {
                var request = byte.Parse(textBoxRequest.Text);
                var responseBuf = new byte[2024];
                var result = USB_ControlTransRead(request, ref responseBuf);
                AppendEventLog("ControlTransfer: " + result);
                if (result)
                {
                    AppendEventLog("Response From Device: " + Encoding.ASCII.GetString(responseBuf));
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ButtonEp03Int_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_ep3InterupEnable)
                {
                    _epReader = _device.OpenEndpointReader(ReadEndpointID.Ep03, 256, EndpointType.Interrupt);
                    _epReader.DataReceived += EndpointReader_DataReceived;
                    _epReader.DataReceivedEnabled = true;

                    //var data = Encoding.ASCII.GetBytes("Test String");
                    //var lenTransfer = 0;
                    //_epWriter = MyUsbDevice.OpenEndpointWriter(WriteEndpointID.Ep03, EndpointType.Interrupt);
                    //_epWriter.Write(data, 500, out lenTransfer);

                    _ep3InterupEnable = true;
                    AppendEventLog("End Point 03 Interupt Enable");
                }
                else
                {
                    _ep3InterupEnable = false;
                    _epReader.DataReceivedEnabled = false;
                    _epReader.DataReceived -= EndpointReader_DataReceived;
                    _epReader.Abort();
                    AppendEventLog("End Point 03 Interupt Disable");
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void DataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                if (dataGridView1.SelectedRows.Count <= 0) return;
                var index = dataGridView1.SelectedRows[0].Index;
                var dev = _devices[index];
                tsTextBoxVidPid.Text = string.Format("{0:X4}:{1:X4}", dev.Vid, dev.Pid);
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion


    }
}
