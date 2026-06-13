import re
with open('MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

old_sig = 'public static string DumpHidReportDescriptor(string devicePath)'
new_sig = 'public static string DumpHidReportDescriptor(IntPtr hDevice)'

if old_sig in content:
    # Find the start and end of the method
    start = content.find(old_sig)
    # Find the closing brace at the same indentation level + newline
    # The method ends with "    }" on its own line followed by blank+next method
    end_marker = '\n        return sb.ToString();\n    }\n'
    end = content.find(end_marker, start)
    if end >= 0:
        end = end + len(end_marker)
        new_body = '''    public static string DumpHidReportDescriptor(IntPtr hDevice)
    {
        var sb = new System.Text.StringBuilder();

        uint size = 0;
        GetRawInputDeviceInfoA(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size == 0)
        {
            sb.AppendLine($"无法获取 preparsed data, error={GetLastError()}");
            return sb.ToString();
        }

        IntPtr pPreparsed = Marshal.AllocHGlobal((int)size);
        try
        {
            GetRawInputDeviceInfoA(hDevice, RIDI_PREPARSEDDATA, pPreparsed, ref size);
            if (size == 0) return sb.ToString();

            if (HidP_GetCaps(pPreparsed, out var caps) >= 0)
                sb.AppendLine($"HID Caps: 输入报告={caps.InputReportByteLength}B");

            uint capsLen = 0;
            HidP_GetValueCaps(HidP_Input, IntPtr.Zero, ref capsLen, pPreparsed);
            if (capsLen > 0)
            {
                int cs = Marshal.SizeOf<HIDP_VALUE_CAPS>();
                IntPtr pc = Marshal.AllocHGlobal((int)(capsLen * cs));
                try
                {
                    HidP_GetValueCaps(HidP_Input, pc, ref capsLen, pPreparsed);
                    for (uint i = 0; i < capsLen; i++)
                    {
                        var vc = Marshal.PtrToStructure<HIDP_VALUE_CAPS>(IntPtr.Add(pc, (int)(i * cs)));
                        sb.AppendLine($"  [{i}] Page=0x{vc.UsagePage:X2}" +
                            $" 位宽={vc.BitSize}bit 数量={vc.ReportCount}" +
                            $" 范围=[{vc.LogicalMin},{vc.LogicalMax}]" +
                            $" ReportID={vc.ReportID}");
                    }
                }
                finally { Marshal.FreeHGlobal(pc); }
            }
            else sb.AppendLine("无 Value Caps");
        }
        finally { Marshal.FreeHGlobal(pPreparsed); }

        return sb.ToString();
    }
'''
        content = content[:start] + new_body + content[end:]
        with open('MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
            f.write(content)
        print('REPLACED SUCCESSFULLY')
    else:
        print('End marker not found')
else:
    print(f'Signature not found')
