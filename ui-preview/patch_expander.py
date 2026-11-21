# -*- coding: utf-8 -*-
import io, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
P = os.path.join(ROOT, "src/DiagnosticFlashTool.App/Styles/Shell/ShellStyles.xaml")
s = io.open(P, encoding="utf-8").read()
eol = "\r\n" if "\r\n" in s else "\n"

OLD = '''    <Style x:Key="ShellProjectExpanderStyle" TargetType="Expander">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="HorizontalContentAlignment" Value="Stretch" />
        <Setter Property="Foreground" Value="{StaticResource SidebarTextBrush}" />
        <Setter Property="FontSize" Value="{StaticResource FontSize.Body}" />
        <Setter Property="Margin" Value="4,0,0,0" />
    </Style>'''

NEW = '''    <!--
        项目导航折叠：不用 Fluent 默认的圆形展开按钮（深色侧栏上会变成白色圆点），
        改为无底色的 12px 箭头 + 整行悬停高亮，展开时箭头由 ChevronRight 转为 ChevronDown。
    -->
    <Style x:Key="ShellProjectExpanderStyle" TargetType="Expander">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="HorizontalContentAlignment" Value="Stretch" />
        <Setter Property="Foreground" Value="{StaticResource SidebarTextBrush}" />
        <Setter Property="FontSize" Value="{StaticResource FontSize.Body}" />
        <Setter Property="Margin" Value="4,0,0,0" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Expander">
                    <StackPanel>
                        <ToggleButton x:Name="HeaderToggle"
                                      Focusable="False"
                                      Height="34"
                                      HorizontalContentAlignment="Stretch"
                                      IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                            <ToggleButton.Template>
                                <ControlTemplate TargetType="ToggleButton">
                                    <Border x:Name="HeaderBorder"
                                            Background="Transparent"
                                            CornerRadius="{StaticResource CornerRadius.Compact}">
                                        <Grid Margin="6,0,8,0">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="18" />
                                                <ColumnDefinition Width="*" />
                                            </Grid.ColumnDefinitions>
                                            <controls:FluentIcon x:Name="Chevron"
                                                                 Grid.Column="0"
                                                                 Kind="ChevronRight"
                                                                 Width="12"
                                                                 Height="12"
                                                                 FontSize="11"
                                                                 Foreground="{StaticResource SidebarMutedTextBrush}"
                                                                 HorizontalAlignment="Center"
                                                                 VerticalAlignment="Center" />
                                            <ContentPresenter Grid.Column="1"
                                                              VerticalAlignment="Center"
                                                              ContentSource="Content" />
                                        </Grid>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter TargetName="HeaderBorder" Property="Background" Value="{StaticResource SidebarHoverBrush}" />
                                            <Setter TargetName="Chevron" Property="Foreground" Value="{StaticResource SidebarTextBrush}" />
                                        </Trigger>
                                        <Trigger Property="IsChecked" Value="True">
                                            <Setter TargetName="Chevron" Property="Kind" Value="ChevronDown" />
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </ToggleButton.Template>
                            <ContentPresenter ContentSource="Header" />
                        </ToggleButton>
                        <ContentPresenter x:Name="ExpandSite" Visibility="Collapsed" />
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsExpanded" Value="True">
                            <Setter TargetName="ExpandSite" Property="Visibility" Value="Visible" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>'''

o = OLD.replace("\n", eol); n = NEW.replace("\n", eol)
if s.count(o) != 1:
    print("FAIL count", s.count(o)); sys.exit(1)
io.open(P, "w", encoding="utf-8", newline="").write(s.replace(o, n))
print("expander template applied")
