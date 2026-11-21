# -*- coding: utf-8 -*-
import io, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "src/DiagnosticFlashTool.App")
fails = []
def read(p): return io.open(p, encoding="utf-8").read()
def write(p, s): io.open(p, "w", encoding="utf-8", newline="").write(s)
def rep(s, old, new, n=1, tag=""):
    eol = "\r\n" if "\r\n" in s else "\n"
    o = old.replace("\n", eol); nw = new.replace("\n", eol)
    c = s.count(o)
    if c != n:
        fails.append("%s: expected %d got %d" % (tag, n, c)); return s
    return s.replace(o, nw)

# ---------- 全局表格样式：执行行 ----------
p = os.path.join(APP, "Styles/Controls/DataGridStyles.xaml")
s = read(p)
s = rep(s, """    <Style x:Key="DataGrid.StatusText" TargetType="TextBlock">""",
"""    <!-- 执行行：当前刷写节点用 3px 左轨 + 浅蓝底高亮，文字保持可读，
         仍由页面通过 ItemContainerStyle 引用，不在页面里自定义行样式。 -->
    <Style x:Key="DataGrid.RowRunning" TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
        <Setter Property="Background" Value="{StaticResource PrimarySoftBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource PrimaryBrush}" />
        <Setter Property="BorderThickness" Value="3,0,0,0" />
    </Style>

    <Style x:Key="DataGrid.StatusText" TargetType="TextBlock">""", tag="rowstyle")
write(p, s)

# ---------- 流程页：执行行样式 + 校验结果语义化 ----------
p = os.path.join(APP, "Views/FlowConfigView.xaml")
s = read(p)
s = rep(s, """                          SelectedItem="{Binding SelectedFlowStep, Mode=TwoWay}"
                          AutoGenerateColumns="False\"""",
"""                          SelectedItem="{Binding SelectedFlowStep, Mode=TwoWay}"
                          ItemContainerStyle="{StaticResource DataGrid.RowRunning}"
                          AutoGenerateColumns="False\"""", tag="itemcontainer")

OLD_CARD = """        <controls:HeaderedCard Grid.Row="4"
                               Header="校验结果"
                               Style="{StaticResource HeaderedCard.Panel}">
            <ScrollViewer VerticalScrollBarVisibility="Hidden"
                          HorizontalScrollBarVisibility="Disabled">
                <TextBlock Text="{Binding FlowValidationText}"
                           FontFamily="{StaticResource Font.Mono}"
                           FontSize="{StaticResource FontSize.Assist}"
                           Foreground="{StaticResource BodyTextBrush}"
                           TextWrapping="Wrap" />
            </ScrollViewer>
        </controls:HeaderedCard>"""

NEW_CARD = """        <controls:HeaderedCard Grid.Row="4"
                               Header="校验结果"
                               Style="{StaticResource HeaderedCard.Panel}">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <!-- 状态胶囊：底色、图标与文字同时表达结果，不单靠颜色。 -->
                <Border Grid.Row="0"
                        Height="24"
                        Padding="8,0"
                        Margin="0,0,0,6"
                        HorizontalAlignment="Left"
                        BorderThickness="1">
                    <Border.Style>
                        <Style TargetType="Border">
                            <Setter Property="Background" Value="{StaticResource StatusNeutralSoftBrush}" />
                            <Setter Property="BorderBrush" Value="{StaticResource StatusNeutralBorderBrush}" />
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding FlowValidationStateKind}" Value="Success">
                                    <Setter Property="Background" Value="{StaticResource StatusSuccessSoftBrush}" />
                                    <Setter Property="BorderBrush" Value="{StaticResource StatusSuccessBorderBrush}" />
                                </DataTrigger>
                                <DataTrigger Binding="{Binding FlowValidationStateKind}" Value="Warning">
                                    <Setter Property="Background" Value="{StaticResource StatusWarningSoftBrush}" />
                                    <Setter Property="BorderBrush" Value="{StaticResource StatusWarningBorderBrush}" />
                                </DataTrigger>
                                <DataTrigger Binding="{Binding FlowValidationStateKind}" Value="Error">
                                    <Setter Property="Background" Value="{StaticResource StatusErrorSoftBrush}" />
                                    <Setter Property="BorderBrush" Value="{StaticResource StatusErrorBorderBrush}" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Border.Style>

                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <TextBlock Text="\\u25cf" Margin="0,0,6,0" VerticalAlignment="Center">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="FontSize" Value="{StaticResource FontSize.Caption}" />
                                    <Setter Property="Foreground" Value="{StaticResource StatusNeutralBrush}" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding FlowValidationStateKind}" Value="Success">
                                            <Setter Property="Foreground" Value="{StaticResource StatusSuccessBrush}" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding FlowValidationStateKind}" Value="Warning">
                                            <Setter Property="Foreground" Value="{StaticResource StatusWarningBrush}" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding FlowValidationStateKind}" Value="Error">
                                            <Setter Property="Foreground" Value="{StaticResource StatusErrorBrush}" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>

                        <TextBlock Text="{Binding FlowValidationStatusText}" FontWeight="Medium" VerticalAlignment="Center">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="FontSize" Value="{StaticResource FontSize.Caption}" />
                                    <Setter Property="Foreground" Value="{StaticResource StatusNeutralBrush}" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding FlowValidationStateKind}" Value="Success">
                                            <Setter Property="Foreground" Value="{StaticResource StatusSuccessBrush}" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding FlowValidationStateKind}" Value="Warning">
                                            <Setter Property="Foreground" Value="{StaticResource StatusWarningBrush}" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding FlowValidationStateKind}" Value="Error">
                                            <Setter Property="Foreground" Value="{StaticResource StatusErrorBrush}" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>

                        <TextBlock Text="{Binding FlowValidationSummaryText}"
                                   Margin="8,0,0,0"
                                   VerticalAlignment="Center"
                                   FontSize="{StaticResource FontSize.Caption}"
                                   Foreground="{StaticResource SubtleTextBrush}" />
                    </StackPanel>
                </Border>

                <ScrollViewer Grid.Row="1"
                              VerticalScrollBarVisibility="Hidden"
                              HorizontalScrollBarVisibility="Disabled">
                    <ItemsControl ItemsSource="{Binding FlowValidationLines}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,0,0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="18" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>

                                    <TextBlock Grid.Column="0" Text="{Binding Icon}" VerticalAlignment="Top">
                                        <TextBlock.Style>
                                            <Style TargetType="TextBlock">
                                                <Setter Property="FontSize" Value="{StaticResource FontSize.Assist}" />
                                                <Setter Property="Foreground" Value="{StaticResource SubtleTextBrush}" />
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding IsWarning}" Value="True">
                                                        <Setter Property="Foreground" Value="{StaticResource StatusWarningBrush}" />
                                                    </DataTrigger>
                                                    <DataTrigger Binding="{Binding IsError}" Value="True">
                                                        <Setter Property="Foreground" Value="{StaticResource StatusErrorBrush}" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBlock.Style>
                                    </TextBlock>

                                    <TextBlock Grid.Column="1" Text="{Binding Text}" TextWrapping="Wrap">
                                        <TextBlock.Style>
                                            <Style TargetType="TextBlock">
                                                <Setter Property="FontFamily" Value="{StaticResource Font.Mono}" />
                                                <Setter Property="FontSize" Value="{StaticResource FontSize.Assist}" />
                                                <Setter Property="Foreground" Value="{StaticResource BodyTextBrush}" />
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding IsWarning}" Value="True">
                                                        <Setter Property="Foreground" Value="{StaticResource StatusWarningBrush}" />
                                                    </DataTrigger>
                                                    <DataTrigger Binding="{Binding IsError}" Value="True">
                                                        <Setter Property="Foreground" Value="{StaticResource StatusErrorBrush}" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBlock.Style>
                                    </TextBlock>
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Grid>
        </controls:HeaderedCard>"""

s = rep(s, OLD_CARD, NEW_CARD, tag="validation-card")
write(p, s)

if fails:
    print("FAILED")
    for f in fails: print(" -", f)
    sys.exit(1)
print("xaml patched")
