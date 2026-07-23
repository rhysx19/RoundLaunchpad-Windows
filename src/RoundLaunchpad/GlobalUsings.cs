// Prefer WPF types when WinForms/System.Drawing are also referenced (tray icon).
global using Application = System.Windows.Application;
global using Color = System.Windows.Media.Color;
global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
global using Image = System.Windows.Controls.Image;
global using Brushes = System.Windows.Media.Brushes;
global using Button = System.Windows.Controls.Button;
global using CheckBox = System.Windows.Controls.CheckBox;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Cursors = System.Windows.Input.Cursors;
global using Orientation = System.Windows.Controls.Orientation;
global using MessageBox = System.Windows.MessageBox;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;
