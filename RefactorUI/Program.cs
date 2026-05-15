using System;
using System.IO;
using System.Text;

class Program
{
    static string src = @"C:\Users\Mirko\Documents\Orthoplanner\src\OrthoPlanner.App\MainWindow.xaml";

    static void Main()
    {
        var lines = File.ReadAllLines(src, Encoding.UTF8);
        Console.WriteLine($"Loaded {lines.Length} lines.");

        var result = new System.Collections.Generic.List<string>(lines.Length + 200);
        int i = 0;
        int changes = 0;

        while (i < lines.Length)
        {
            string line = lines[i];

            // ── CHANGE 1: inject converters + PanelTabButtonStyle after BooleanToVisibilityConverter ──
            if (line.Trim() == @"<BooleanToVisibilityConverter x:Key=""BooleanToVisibilityConverter"" />")
            {
                result.Add(line);
                result.Add(@"        <cv:IntToVisibilityConverter x:Key=""IntToVisibilityConverter"" />");
                result.Add(@"        <cv:EnumToBoolConverter x:Key=""EnumToBoolConverter"" />");
                result.Add(@"        <Style x:Key=""PanelTabButtonStyle"" TargetType=""RadioButton"">");
                result.Add(@"            <Setter Property=""Background"" Value=""Transparent"" />");
                result.Add(@"            <Setter Property=""Foreground"" Value=""#FFA0AAB5"" />");
                result.Add(@"            <Setter Property=""BorderThickness"" Value=""1,1,1,0"" />");
                result.Add(@"            <Setter Property=""BorderBrush"" Value=""Transparent"" />");
                result.Add(@"            <Setter Property=""Padding"" Value=""12,8"" />");
                result.Add(@"            <Setter Property=""Cursor"" Value=""Hand"" />");
                result.Add(@"            <Setter Property=""Template"">");
                result.Add(@"                <Setter.Value>");
                result.Add(@"                    <ControlTemplate TargetType=""RadioButton"">");
                result.Add(@"                        <Border x:Name=""border"" Background=""{TemplateBinding Background}""");
                result.Add(@"                                BorderThickness=""{TemplateBinding BorderThickness}""");
                result.Add(@"                                BorderBrush=""{TemplateBinding BorderBrush}""");
                result.Add(@"                                CornerRadius=""4,4,0,0"">");
                result.Add(@"                            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>");
                result.Add(@"                        </Border>");
                result.Add(@"                        <ControlTemplate.Triggers>");
                result.Add(@"                            <Trigger Property=""IsMouseOver"" Value=""True"">");
                result.Add(@"                                <Setter TargetName=""border"" Property=""Background"" Value=""#FF2B3240"" />");
                result.Add(@"                                <Setter Property=""Foreground"" Value=""White"" />");
                result.Add(@"                            </Trigger>");
                result.Add(@"                            <Trigger Property=""IsChecked"" Value=""True"">");
                result.Add(@"                                <Setter TargetName=""border"" Property=""Background"" Value=""#FF3B4559"" />");
                result.Add(@"                                <Setter TargetName=""border"" Property=""BorderBrush"" Value=""#FF424D63"" />");
                result.Add(@"                                <Setter Property=""Foreground"" Value=""White"" />");
                result.Add(@"                            </Trigger>");
                result.Add(@"                        </ControlTemplate.Triggers>");
                result.Add(@"                    </ControlTemplate>");
                result.Add(@"                </Setter.Value>");
                result.Add(@"            </Setter>");
                result.Add(@"        </Style>");
                i++; changes++;
                Console.WriteLine("[1] Converters + PanelTabButtonStyle injected.");
                continue;
            }

            // ── CHANGE 2: remove "Surgical Movements" left expander ──
            // Detect its opening line; skip until we hit the next expander block (Splint Generation)
            if (line.Contains("<!-- 5. SURGICAL MOVEMENTS -->"))
            {
                // skip until we find <!-- 6. SPLINT GENERATION --> (exclusive)
                while (i < lines.Length && !lines[i].Contains("<!-- 6. SPLINT GENERATION -->"))
                    i++;
                changes++;
                Console.WriteLine("[2] Surgical Movements left expander removed.");
                // do NOT advance i; the next loop iteration will handle the splint line
                continue;
            }

            // ── CHANGE 3: Add context menu to 3D Models segment list border ──
            // The unique signature: inside Segments ItemsControl DataTemplate, a Border with 10FFFFFF
            // followed immediately by <DockPanel> and <CheckBox IsChecked="{Binding IsVisible}"
            // We detect the border line AND confirm context by checking next few lines
            if (line.Contains(@"Background=""#10FFFFFF""") && i + 2 < lines.Length
                && lines[i+1].Trim().StartsWith("<DockPanel>")
                && lines[i+2].Trim().Contains(@"IsChecked=""{Binding IsVisible}"""))
            {
                // Check if this is inside the Segments block (not ImportedMeshes)
                // Count leading spaces on line
                string indent = GetIndent(line);
                result.Add(line); // <Border ...>
                result.Add(indent + "    <Border.ContextMenu>");
                result.Add(indent + "        <ContextMenu Background=\"#FF1E222A\" BorderThickness=\"1\" BorderBrush=\"#FF30343D\" Foreground=\"White\">");
                result.Add(indent + "            <MenuItem StaysOpenOnClick=\"True\" Focusable=\"False\" Background=\"Transparent\" Padding=\"0\">");
                result.Add(indent + "                <MenuItem.Header>");
                result.Add(indent + "                    <StackPanel Width=\"170\" Margin=\"2\">");
                result.Add(indent + "                        <TextBlock Text=\"Transparency\" FontSize=\"11\" Foreground=\"#FFA0AAB5\" Margin=\"0,0,0,4\"/>");
                result.Add(indent + "                        <DockPanel>");
                result.Add(indent + "                            <TextBox DockPanel.Dock=\"Right\" Width=\"45\" Margin=\"4,0,0,0\" Text=\"{Binding OpacityPercent, StringFormat=\\{0:F0\\}, UpdateSourceTrigger=LostFocus}\" TextAlignment=\"Center\" Background=\"#FF30343D\" Foreground=\"White\" BorderThickness=\"0\" FontSize=\"11\" VerticalAlignment=\"Center\" />");
                result.Add(indent + "                            <Slider Minimum=\"0\" Maximum=\"100\" Value=\"{Binding OpacityPercent}\" VerticalAlignment=\"Center\"/>");
                result.Add(indent + "                        </DockPanel>");
                result.Add(indent + "                    </StackPanel>");
                result.Add(indent + "                </MenuItem.Header>");
                result.Add(indent + "            </MenuItem>");
                result.Add(indent + "        </ContextMenu>");
                result.Add(indent + "    </Border.ContextMenu>");
                // replace <DockPanel> with <DockPanel Background="Transparent">
                result.Add(indent + "    <DockPanel Background=\"Transparent\">");
                i += 2; // skip <Border> (already added) and <DockPanel>
                changes++;
                Console.WriteLine("[3] Transparency context menu injected.");
                continue;
            }

            // ── CHANGE 4: Remove old SurgicalMovementsOverlay from viewport ──
            if (line.Contains("<!-- Surgical Movements Overlay -->"))
            {
                // skip until end of its Border (the next <!-- tag we care about)
                while (i < lines.Length && !lines[i].Contains("<!-- Overlay: placeholder text when no data -->"))
                    i++;
                changes++;
                Console.WriteLine("[4] Old SurgicalMovementsOverlay removed.");
                continue;
            }

            // ── CHANGE 5: Replace old right column with 3-tab layout ──
            if (line.Contains("<!-- ═══ RIGHT: 2D SLICE VIEWERS ═══ -->"))
            {
                // Skip everything until the photogrammetry overlay comment
                string rightColumnContent = BuildRightColumn();
                result.Add(rightColumnContent);
                result.Add("");
                while (i < lines.Length && !lines[i].Contains("<!-- ═══ FULLSCREEN PHOTOGRAMMETRY OVERLAY ═══ -->"))
                    i++;
                changes++;
                Console.WriteLine("[5] New 3-tab right column injected.");
                continue;
            }

            // ── CHANGE 6: add IsTransparent to the Segments ItemsModel3D ──
            if (line.Contains(@"<hx:MeshGeometryModel3D Geometry=""{Binding Geometry}"" Material=""{Binding Material}"" IsRendering=""{Binding IsVisible}"" Transform=""{Binding Transform}"" />"))
            {
                result.Add(line.Replace(
                    @"Transform=""{Binding Transform}"" />",
                    @"Transform=""{Binding Transform}"" IsTransparent=""{Binding IsTransparent}"" />"));
                i++; changes++;
                Console.WriteLine("[6] IsTransparent added to Segments MeshGeometryModel3D.");
                continue;
            }

            result.Add(line);
            i++;
        }

        File.WriteAllLines(src, result.ToArray(), Encoding.UTF8);
        Console.WriteLine($"\nDone. {changes}/6 changes applied. Output: {result.Count} lines.");
    }

    static string GetIndent(string line)
    {
        int n = 0;
        while (n < line.Length && (line[n] == ' ' || line[n] == '\t')) n++;
        return line.Substring(0, n);
    }

    static string BuildRightColumn()
    {
        // Extract the surgical boxes from the old center overlay that was just removed — 
        // we embed them inline as a string constant here since we know their content
        return @"            <!-- ═══ RIGHT: 3-TAB LAYOUT ═══ -->
            <Grid Grid.Column=""4"">
                <Grid.RowDefinitions>
                    <RowDefinition Height=""Auto"" />
                    <RowDefinition Height=""*"" />
                </Grid.RowDefinitions>

                <!-- TAB HEADER -->
                <Border Grid.Row=""0"" Margin=""4,4,4,0"" Background=""Transparent"" BorderThickness=""0,0,0,1"" BorderBrush=""#FF3B4559"">
                    <UniformGrid Columns=""3"" Margin=""0"">
                        <RadioButton Content=""CT (MPR)"" GroupName=""RightTabs"" IsChecked=""{Binding RightPanelTabIndex, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=0}"" Style=""{StaticResource PanelTabButtonStyle}"" FontSize=""11"" FontWeight=""SemiBold"" Margin=""0,0,2,0""/>
                        <RadioButton Content=""Measurements"" GroupName=""RightTabs"" IsChecked=""{Binding RightPanelTabIndex, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=1}"" Style=""{StaticResource PanelTabButtonStyle}"" FontSize=""11"" FontWeight=""SemiBold"" Margin=""2,0,2,0""/>
                        <RadioButton Content=""Surgery"" GroupName=""RightTabs"" IsChecked=""{Binding RightPanelTabIndex, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=2}"" Style=""{StaticResource PanelTabButtonStyle}"" FontSize=""11"" FontWeight=""SemiBold"" Margin=""2,0,0,0""/>
                    </UniformGrid>
                </Border>

                <!-- TAB 0: CT MPR -->
                <Grid Grid.Row=""1"" Visibility=""{Binding RightPanelTabIndex, Converter={StaticResource IntToVisibilityConverter}, ConverterParameter=0}"">
                    <Grid.RowDefinitions>
                        <RowDefinition Height=""Auto"" />
                        <RowDefinition Height=""{Binding AxialDisplayHeight}"" />
                        <RowDefinition Height=""{Binding CoronalDisplayHeight}"" />
                        <RowDefinition Height=""{Binding SagittalDisplayHeight}"" />
                    </Grid.RowDefinitions>

                    <!-- MPR Toolbar -->
                    <StackPanel Grid.Row=""0"" Orientation=""Horizontal"" Margin=""6,3"">
                        <CheckBox Content=""Crosshairs"" IsChecked=""{Binding ShowCrosshairs, Mode=TwoWay}""
                                  Foreground=""{StaticResource TextSecondaryBrush}"" FontSize=""10""
                                  Checked=""OnCrosshairsToggled"" Unchecked=""OnCrosshairsToggled"" />
                    </StackPanel>

                    <!-- Axial View -->
                    <Border Grid.Row=""1"" Margin=""4"" Background=""{StaticResource BgMediumBrush}""
                            BorderBrush=""{StaticResource BorderBrush}"" BorderThickness=""1"" CornerRadius=""4"">
                        <Grid x:Name=""AxialPanel""
                              MouseWheel=""AxialPanel_MouseWheel""
                              MouseLeftButtonDown=""SlicePanel_LeftDown""
                              MouseRightButtonDown=""SlicePanel_RightDown""
                              MouseRightButtonUp=""SlicePanel_RightUp""
                              MouseMove=""SlicePanel_MouseMove""
                              Background=""Transparent"">
                            <Image Source=""{Binding AxialImage}"" Stretch=""Fill"" RenderOptions.BitmapScalingMode=""NearestNeighbor"" />
                            <Canvas x:Name=""AxialCrosshairCanvas"" IsHitTestVisible=""False"" />
                            <TextBlock Text=""AXIAL"" Style=""{StaticResource PanelHeader}""
                                       HorizontalAlignment=""Left"" VerticalAlignment=""Top"" Margin=""8,6"" />
                            <Button Content=""⤢"" HorizontalAlignment=""Right"" VerticalAlignment=""Top""
                                    Margin=""0,4,8,0"" Padding=""4,1"" FontSize=""12"" Cursor=""Hand""
                                    Background=""Transparent"" BorderThickness=""0"" Foreground=""{StaticResource TextSecondaryBrush}""
                                    Click=""EnlargeAxial_Click"" ToolTip=""Enlarge Axial"" />
                        </Grid>
                    </Border>

                    <!-- Coronal View -->
                    <Border Grid.Row=""2"" Margin=""4"" Background=""{StaticResource BgMediumBrush}""
                            BorderBrush=""{StaticResource BorderBrush}"" BorderThickness=""1"" CornerRadius=""4"">
                        <Grid x:Name=""CoronalPanel""
                              MouseWheel=""CoronalPanel_MouseWheel""
                              MouseLeftButtonDown=""SlicePanel_LeftDown""
                              MouseRightButtonDown=""SlicePanel_RightDown""
                              MouseRightButtonUp=""SlicePanel_RightUp""
                              MouseMove=""SlicePanel_MouseMove""
                              Background=""Transparent"">
                            <Image Source=""{Binding CoronalImage}"" Stretch=""Fill"" RenderOptions.BitmapScalingMode=""NearestNeighbor"" />
                            <Canvas x:Name=""CoronalCrosshairCanvas"" IsHitTestVisible=""False"" />
                            <TextBlock Text=""CORONAL"" Style=""{StaticResource PanelHeader}""
                                       HorizontalAlignment=""Left"" VerticalAlignment=""Top"" Margin=""8,6"" />
                            <Button Content=""⤢"" HorizontalAlignment=""Right"" VerticalAlignment=""Top""
                                    Margin=""0,4,8,0"" Padding=""4,1"" FontSize=""12"" Cursor=""Hand""
                                    Background=""Transparent"" BorderThickness=""0"" Foreground=""{StaticResource TextSecondaryBrush}""
                                    Click=""EnlargeCoronal_Click"" ToolTip=""Enlarge Coronal"" />
                        </Grid>
                    </Border>

                    <!-- Sagittal View -->
                    <Border Grid.Row=""3"" Margin=""4"" Background=""{StaticResource BgMediumBrush}""
                            BorderBrush=""{StaticResource BorderBrush}"" BorderThickness=""1"" CornerRadius=""4"">
                        <Grid x:Name=""SagittalPanel""
                              MouseWheel=""SagittalPanel_MouseWheel""
                              MouseLeftButtonDown=""SlicePanel_LeftDown""
                              MouseRightButtonDown=""SlicePanel_RightDown""
                              MouseRightButtonUp=""SlicePanel_RightUp""
                              MouseMove=""SlicePanel_MouseMove""
                              Background=""Transparent"">
                            <Image Source=""{Binding SagittalImage}"" Stretch=""Fill"" RenderOptions.BitmapScalingMode=""NearestNeighbor"" />
                            <Canvas x:Name=""SagittalCrosshairCanvas"" IsHitTestVisible=""False"" />
                            <TextBlock Text=""SAGITTAL"" Style=""{StaticResource PanelHeader}""
                                       HorizontalAlignment=""Left"" VerticalAlignment=""Top"" Margin=""8,6"" />
                            <Button Content=""⤢"" HorizontalAlignment=""Right"" VerticalAlignment=""Top""
                                    Margin=""0,4,8,0"" Padding=""4,1"" FontSize=""12"" Cursor=""Hand""
                                    Background=""Transparent"" BorderThickness=""0"" Foreground=""{StaticResource TextSecondaryBrush}""
                                    Click=""EnlargeSagittal_Click"" ToolTip=""Enlarge Sagittal"" />
                        </Grid>
                    </Border>
                </Grid>

                <!-- TAB 1: MEASUREMENTS -->
                <Grid Grid.Row=""1"" Visibility=""{Binding RightPanelTabIndex, Converter={StaticResource IntToVisibilityConverter}, ConverterParameter=1}"" Margin=""4,8,4,4"">
                    <Border Background=""#FF1E222A"" BorderBrush=""#FF30343D"" BorderThickness=""1"" CornerRadius=""4"" Padding=""12"">
                        <StackPanel>
                            <TextBlock Text=""CUSTOM MEASUREMENTS"" Foreground=""White"" FontSize=""11"" FontWeight=""Bold"" Margin=""0,0,0,8"" />
                            <TextBlock Text=""Right-click on 3D models or slice views to place distance and angle measurements."" Foreground=""{StaticResource TextSecondaryBrush}"" FontSize=""11"" FontStyle=""Italic"" TextWrapping=""Wrap"" Margin=""0,0,0,16"" />
                            <TextBlock Text=""CEPHALOMETRY"" Foreground=""White"" FontSize=""11"" FontWeight=""Bold"" Margin=""0,0,0,8"" />
                            <TextBlock Text=""Place landmarks, trace cephalometric planes, and compute angular and linear measurements."" Foreground=""{StaticResource TextSecondaryBrush}"" FontSize=""11"" FontStyle=""Italic"" TextWrapping=""Wrap"" />
                        </StackPanel>
                    </Border>
                </Grid>

                <!-- TAB 2: SURGERY -->
                <Grid Grid.Row=""1"" Visibility=""{Binding RightPanelTabIndex, Converter={StaticResource IntToVisibilityConverter}, ConverterParameter=2}"" Margin=""4,8,4,4"">
                    <Border x:Name=""SurgeryTabContainer"" Background=""Transparent"">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height=""Auto""/>
                                <RowDefinition Height=""*""/>
                            </Grid.RowDefinitions>
                            <Border BorderBrush=""{StaticResource BorderBrush}"" BorderThickness=""0,0,0,1"" Padding=""8"">
                                <DockPanel>
                                    <TextBlock Text=""SURGICAL MOVEMENTS"" FontSize=""12"" FontWeight=""Bold"" Foreground=""White"" VerticalAlignment=""Center""/>
                                    <Button Content=""✕"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"" DockPanel.Dock=""Right""
                                            Margin=""0"" Padding=""4,2"" FontSize=""12"" Cursor=""Hand""
                                            Background=""Transparent"" BorderThickness=""0""
                                            Foreground=""{StaticResource TextSecondaryBrush}""
                                            Command=""{Binding CloseSurgicalMovementsCommand}"" ToolTip=""Close Surgery Pane"" />
                                </DockPanel>
                            </Border>
                            <ScrollViewer Grid.Row=""1"" VerticalScrollBarVisibility=""Auto"" Padding=""8"">
                                <StackPanel>
                                    <StackPanel.Resources>
                                        <Style x:Key=""SurgeryStepperButtonStyle"" TargetType=""RepeatButton"">
                                            <Setter Property=""Background"" Value=""#FF30343D"" />
                                            <Setter Property=""Foreground"" Value=""White"" />
                                            <Setter Property=""Width"" Value=""20"" />
                                            <Setter Property=""Height"" Value=""12"" />
                                            <Setter Property=""BorderThickness"" Value=""0"" />
                                            <Setter Property=""FontSize"" Value=""8"" />
                                            <Setter Property=""Template"">
                                                <Setter.Value>
                                                    <ControlTemplate TargetType=""RepeatButton"">
                                                        <Border Background=""{TemplateBinding Background}"" CornerRadius=""1"">
                                                            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" />
                                                        </Border>
                                                    </ControlTemplate>
                                                </Setter.Value>
                                            </Setter>
                                        </Style>
                                        <Style x:Key=""SurgeryTextBoxStyle"" TargetType=""TextBox"">
                                            <Setter Property=""Background"" Value=""#FF30343D"" />
                                            <Setter Property=""Foreground"" Value=""White"" />
                                            <Setter Property=""BorderThickness"" Value=""0"" />
                                            <Setter Property=""TextAlignment"" Value=""Center"" />
                                            <Setter Property=""VerticalAlignment"" Value=""Center"" />
                                            <Setter Property=""Height"" Value=""25""/>
                                            <Setter Property=""Width"" Value=""40""/>
                                            <Setter Property=""Margin"" Value=""0,0,4,0""/>
                                        </Style>
                                    </StackPanel.Resources>

                                    <Button Content=""Load &amp; Align Occlusion STLs"" Command=""{Binding OpenOcclusionAlignmentCommand}"" Margin=""0,0,0,8"" />

                                    <!-- Maxilla Box -->
                                    <Border Background=""#FF1E222A"" BorderBrush=""#FF30343D"" BorderThickness=""1"" CornerRadius=""4"" Padding=""8"" Margin=""0,0,0,8"">
                                        <StackPanel>
                                            <CheckBox Content=""Maxilla-based"" IsChecked=""{Binding IsMaxillaBasedSurgery}"" Foreground=""{StaticResource TextSecondaryBrush}"" FontSize=""10"" Margin=""0,0,0,6""/>
                                            <TextBlock Text=""MAXILLA"" Foreground=""White"" FontSize=""11"" FontWeight=""Bold"" Margin=""0,0,0,8"" />
                                            <Grid Margin=""0,0,0,4"">
                                                <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions>
                                                <StackPanel Grid.Column=""0""><TextBlock Text=""AP (Y)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMaxillaAnt, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMaxillaMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaAnt+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMaxillaMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaAnt-"" IsEnabled=""{Binding IsMaxillaMoveable}""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""1""><TextBlock Text=""Lat (X)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMaxillaLat, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMaxillaMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaLat+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMaxillaMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaLat-"" IsEnabled=""{Binding IsMaxillaMoveable}""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""2""><TextBlock Text=""Vert (Z)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMaxillaVert, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMaxillaMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaVert+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMaxillaMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaVert-"" IsEnabled=""{Binding IsMaxillaMoveable}""/></StackPanel></StackPanel></StackPanel>
                                            </Grid>
                                            <Grid>
                                                <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions>
                                                <StackPanel Grid.Column=""0""><TextBlock Text=""Roll (Y)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMaxillaRoll, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMaxillaMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaRoll+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMaxillaMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaRoll-"" IsEnabled=""{Binding IsMaxillaMoveable}""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""1""><TextBlock Text=""Pitch (X)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMaxillaPitch, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMaxillaMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaPitch+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMaxillaMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaPitch-"" IsEnabled=""{Binding IsMaxillaMoveable}""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""2""><TextBlock Text=""Yaw (Z)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMaxillaYaw, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMaxillaMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaYaw+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMaxillaMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MaxillaYaw-"" IsEnabled=""{Binding IsMaxillaMoveable}""/></StackPanel></StackPanel></StackPanel>
                                            </Grid>
                                        </StackPanel>
                                    </Border>

                                    <!-- Mandible Box -->
                                    <Border Background=""#FF1E222A"" BorderBrush=""#FF30343D"" BorderThickness=""1"" CornerRadius=""4"" Padding=""8"" Margin=""0,0,0,8"">
                                        <StackPanel>
                                            <TextBlock Text=""MANDIBLE"" Foreground=""White"" FontSize=""11"" FontWeight=""Bold"" Margin=""0,0,0,8"" />
                                            <Grid Margin=""0,0,0,4"">
                                                <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions>
                                                <StackPanel Grid.Column=""0""><TextBlock Text=""AP (Y)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMandibleAnt, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMandibleMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleAnt+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMandibleMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleAnt-"" IsEnabled=""{Binding IsMandibleMoveable}""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""1""><TextBlock Text=""Lat (X)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMandibleLat, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMandibleMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleLat+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMandibleMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleLat-"" IsEnabled=""{Binding IsMandibleMoveable}""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""2""><TextBlock Text=""Vert (Z)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMandibleVert, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMandibleMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleVert+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMandibleMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleVert-"" IsEnabled=""{Binding IsMandibleMoveable}""/></StackPanel></StackPanel></StackPanel>
                                            </Grid>
                                            <Grid>
                                                <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions>
                                                <StackPanel Grid.Column=""0""><TextBlock Text=""Roll (Y)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMandibleRoll, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMandibleMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleRoll+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMandibleMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleRoll-"" IsEnabled=""{Binding IsMandibleMoveable}""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""1""><TextBlock Text=""Pitch (X)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMandiblePitch, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMandibleMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandiblePitch+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMandibleMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandiblePitch-"" IsEnabled=""{Binding IsMandibleMoveable}""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""2""><TextBlock Text=""Yaw (Z)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center"" Margin=""0,0,0,2""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgMandibleYaw, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}"" IsEnabled=""{Binding IsMandibleMoveable}""/><StackPanel Orientation=""Vertical"" VerticalAlignment=""Center""><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleYaw+"" Margin=""0,0,0,1"" IsEnabled=""{Binding IsMandibleMoveable}""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""MandibleYaw-"" IsEnabled=""{Binding IsMandibleMoveable}""/></StackPanel></StackPanel></StackPanel>
                                            </Grid>
                                        </StackPanel>
                                    </Border>

                                    <!-- Right Ramus Box -->
                                    <Border Background=""#FF1E222A"" BorderBrush=""#FF30343D"" BorderThickness=""1"" CornerRadius=""4"" Padding=""8"" Margin=""0,0,0,8"">
                                        <StackPanel>
                                            <TextBlock Text=""RIGHT RAMUS"" Foreground=""White"" FontSize=""11"" FontWeight=""Bold"" Margin=""0,0,0,8"" />
                                            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions>
                                                <StackPanel Grid.Column=""0""><TextBlock Text=""Roll (Y)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgRightRamusRoll, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""RightRamusRoll+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""RightRamusRoll-""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""1""><TextBlock Text=""Pitch (X)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgRightRamusPitch, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""RightRamusPitch+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""RightRamusPitch-""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""2""><TextBlock Text=""Yaw (Z)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgRightRamusYaw, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""RightRamusYaw+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""RightRamusYaw-""/></StackPanel></StackPanel></StackPanel>
                                            </Grid>
                                        </StackPanel>
                                    </Border>

                                    <!-- Left Ramus Box -->
                                    <Border Background=""#FF1E222A"" BorderBrush=""#FF30343D"" BorderThickness=""1"" CornerRadius=""4"" Padding=""8"" Margin=""0,0,0,8"">
                                        <StackPanel>
                                            <TextBlock Text=""LEFT RAMUS"" Foreground=""White"" FontSize=""11"" FontWeight=""Bold"" Margin=""0,0,0,8"" />
                                            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions>
                                                <StackPanel Grid.Column=""0""><TextBlock Text=""Roll (Y)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgLeftRamusRoll, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""LeftRamusRoll+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""LeftRamusRoll-""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""1""><TextBlock Text=""Pitch (X)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgLeftRamusPitch, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""LeftRamusPitch+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""LeftRamusPitch-""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""2""><TextBlock Text=""Yaw (Z)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgLeftRamusYaw, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""LeftRamusYaw+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""LeftRamusYaw-""/></StackPanel></StackPanel></StackPanel>
                                            </Grid>
                                        </StackPanel>
                                    </Border>

                                    <!-- Chin Box -->
                                    <Border Background=""#FF1E222A"" BorderBrush=""#FF30343D"" BorderThickness=""1"" CornerRadius=""4"" Padding=""8"" Margin=""0,0,0,8"">
                                        <StackPanel>
                                            <TextBlock Text=""CHIN"" Foreground=""White"" FontSize=""11"" FontWeight=""Bold"" Margin=""0,0,0,8"" />
                                            <Grid Margin=""0,0,0,4""><Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions>
                                                <StackPanel Grid.Column=""0""><TextBlock Text=""AP (Y)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgChinAnt, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinAnt+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinAnt-""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""1""><TextBlock Text=""Lat (X)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgChinLat, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinLat+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinLat-""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""2""><TextBlock Text=""Vert (Z)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgChinVert, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinVert+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinVert-""/></StackPanel></StackPanel></StackPanel>
                                            </Grid>
                                            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions>
                                                <StackPanel Grid.Column=""0""><TextBlock Text=""Roll (Y)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgChinRoll, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinRoll+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinRoll-""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""1""><TextBlock Text=""Pitch (X)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgChinPitch, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinPitch+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinPitch-""/></StackPanel></StackPanel></StackPanel>
                                                <StackPanel Grid.Column=""2""><TextBlock Text=""Yaw (Z)"" FontSize=""9"" Foreground=""#FFA0AAB5"" HorizontalAlignment=""Center""/><StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Center""><TextBox Text=""{Binding SurgChinYaw, StringFormat={}{0:F1}}"" Style=""{StaticResource SurgeryTextBoxStyle}""/><StackPanel><RepeatButton Content=""▲"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinYaw+"" Margin=""0,0,0,1""/><RepeatButton Content=""▼"" Style=""{StaticResource SurgeryStepperButtonStyle}"" Command=""{Binding AdjustSurgeryCommand}"" CommandParameter=""ChinYaw-""/></StackPanel></StackPanel></StackPanel>
                                            </Grid>
                                        </StackPanel>
                                    </Border>

                                </StackPanel>
                            </ScrollViewer>
                        </Grid>
                    </Border>
                </Grid>
            </Grid>";
    }
}
