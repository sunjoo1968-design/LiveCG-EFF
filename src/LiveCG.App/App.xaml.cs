using System.Windows;
using System.Windows.Media.Imaging;

namespace LiveCG.App;
public partial class App : Application
{
    public App()
    {
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is Window window&&window.Icon is null)window.Icon=new BitmapImage(new Uri("pack://application:,,,/LiveCG-EFF;component/Assets/LiveCG.ico"));}));
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        { MessageBox.Show(args.Exception.Message,"LiveCG-EFF · 오류",MessageBoxButton.OK,MessageBoxImage.Error);args.Handled=true; };
        base.OnStartup(e);
    }
}
