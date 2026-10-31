using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCG.Core;

namespace LiveCG.App;

public sealed class ShortcutWindow:Window
{
    public EditorPreferences Preferences { get; private set; }
    public ShortcutWindow(EditorPreferences source)
    {
        Preferences=source with{};Title="영역 단축키 · "+ProductInfo.Credit;Width=500;Height=390;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(23,33,45));Foreground=Brushes.White;
        var panel=new StackPanel{Margin=new Thickness(20)};Content=panel;
        var enabled=new CheckBox{Content="영역 선택 단축키 사용",IsChecked=source.ShortcutsEnabled};panel.Children.Add(enabled);
        ComboBox Choice(string title,string selected)
        {
            panel.Children.Add(new TextBlock{Text=title,Margin=new Thickness(4,10,0,2)});var box=new ComboBox();
            foreach(var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")box.Items.Add(letter.ToString());box.SelectedItem=selected;panel.Children.Add(box);return box;
        }
        var draw=Choice("새 영역 그리기",source.DrawKey);var select=Choice("영역 선택 모드",source.SelectKey);
        panel.Children.Add(new TextBlock{Text="Tab / Shift+Tab: 미리보기·타임라인에서 다음/이전 영역\n1~9: 해당 번호 영역 선택\nCtrl+C/V: 클립 복사/붙여넣기\nCtrl+Z / Ctrl+Shift+Z: 되돌리기/다시 실행\n텍스트 입력 중에는 문서 편집 단축키를 유지합니다.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,12,0,5)});
        var apply=new Button{Content="적용",HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(apply);
        apply.Click+=(_,_)=>
        {
            var candidate=source with{ShortcutsEnabled=enabled.IsChecked==true,DrawKey=(string)draw.SelectedItem,SelectKey=(string)select.SelectedItem};
            try{candidate.Validate();Preferences=candidate;DialogResult=true;}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"단축키 확인");}
        };
    }
}
