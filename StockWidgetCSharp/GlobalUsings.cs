// 全局 using 别名：解决启用 WinForms（仅用 ColorDialog）后 WPF/WinForms 类型名冲突。
// WPF 类型优先；WinForms 仅在 SettingsDialog 里用完全限定名调用 ColorDialog。
global using Application = System.Windows.Application;
global using Color = System.Windows.Media.Color;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Orientation = System.Windows.Controls.Orientation;
global using Brushes = System.Windows.Media.Brushes;
global using Pen = System.Windows.Media.Pen;
global using Point = System.Windows.Point;
global using FontFamily = System.Windows.Media.FontFamily;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;
global using ComboBox = System.Windows.Controls.ComboBox;
global using CheckBox = System.Windows.Controls.CheckBox;
global using ListBox = System.Windows.Controls.ListBox;
global using Button = System.Windows.Controls.Button;
global using Slider = System.Windows.Controls.Slider;
global using TextBox = System.Windows.Controls.TextBox;
global using TextBlock = System.Windows.Controls.TextBlock;
global using GroupBox = System.Windows.Controls.GroupBox;
global using TabControl = System.Windows.Controls.TabControl;
global using TabItem = System.Windows.Controls.TabItem;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
