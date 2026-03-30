import sys

path = r'c:\Users\Mirko\Documents\Orthoplanner\src\OrthoPlanner.App\MainWindow.xaml'

with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

# 1. Extract Surgical Movements Overlay
start_overlay = text.find('<!-- Surgical Movements Overlay -->')
end_overlay = text.find('<!-- Overlay: placeholder text when no data -->')
if start_overlay == -1 or end_overlay == -1:
    print("Could not find overlay bounds")
    sys.exit(1)

overlay_text = text[start_overlay:end_overlay].strip()

# Clean up overlay
overlay_text = overlay_text.replace('Visibility="{Binding IsSurgicalMovementsOpen, Converter={StaticResource BooleanToVisibilityConverter}}" Margin="0" HorizontalAlignment="Right" Width="280"',
                                  'HorizontalAlignment="Stretch" Margin="0"')
overlay_text = overlay_text.replace('Background="#D90C1018"', 'Background="Transparent"')
overlay_text = overlay_text.replace('Grid.Row="1"', '')

# Remove the original overlay from viewport center
text = text[:start_overlay] + text[end_overlay:]

# 2. Rebuild Right Column
col4_start = text.find('<!-- ═══ RIGHT: 2D SLICE VIEWERS ═══ -->')
if col4_start == -1:
    print("Could not find right column")
    sys.exit(1)

photogrammetry_start = text.find('<!-- ═══ FULLSCREEN PHOTOGRAMMETRY OVERLAY ═══ -->')
if photogrammetry_start == -1:
    print("Could not find photogrammetry overlay")
    sys.exit(1)

right_column_old = text[col4_start:photogrammetry_start]

# We want to keep everything from <!-- Axial View --> down to the end of Sagittal View
axial_start = right_column_old.find('<!-- Axial View -->')
axial_end = right_column_old.find('<!-- Coronal View -->')
coronal_end = right_column_old.find('<!-- Sagittal View -->')
sagittal_end = right_column_old.find('</Border>', right_column_old.find('<!-- Sagittal View -->')) + 9

if axial_start == -1 or sagittal_end == -1:
    print("Could not find MPR views")
    sys.exit(1)

axial_view = right_column_old[axial_start:axial_end]
coronal_view = right_column_old[axial_end:coronal_end]
sagittal_view = right_column_old[coronal_end:sagittal_end]

new_right_col = '''<!-- ═══ RIGHT: 3-TAB LAYOUT ═══ -->
            <Grid Grid.Column="4">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <!-- TAB HEADER -->
                <Border Grid.Row="0" Margin="4,4,4,0" Background="#FF1E222A" CornerRadius="4" BorderThickness="1" BorderBrush="{StaticResource BorderBrush}">
                    <UniformGrid Columns="3" Margin="2">
                        <RadioButton Content="CT (MPR)" IsChecked="{Binding RightPanelTabIndex, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=0}" Style="{StaticResource DarkToggleButtonStyle}" FontSize="11" FontWeight="SemiBold" Margin="2"/>
                        <RadioButton Content="Measurements" IsChecked="{Binding RightPanelTabIndex, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=1}" Style="{StaticResource DarkToggleButtonStyle}" FontSize="11" FontWeight="SemiBold" Margin="2"/>
                        <RadioButton Content="Surgery" IsChecked="{Binding RightPanelTabIndex, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=2}" Style="{StaticResource DarkToggleButtonStyle}" FontSize="11" FontWeight="SemiBold" Margin="2"/>
                    </UniformGrid>
                </Border>

                <!-- TAB 0: CT MPR -->
                <Grid Grid.Row="1" Visibility="{Binding RightPanelTabIndex, Converter={StaticResource IntToVisibilityConverter}, ConverterParameter=0}">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <!-- Bind Star ratios directly to the scaled voxel spread for true 1:1 screen mapping -->
                        <RowDefinition Height="{Binding AxialDisplayHeight}" />
                        <RowDefinition Height="{Binding CoronalDisplayHeight}" />
                        <RowDefinition Height="{Binding SagittalDisplayHeight}" />
                    </Grid.RowDefinitions>

                    <!-- MPR Toolbar -->
                    <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="6,3">
                        <CheckBox Content="Crosshairs" IsChecked="{Binding ShowCrosshairs, Mode=TwoWay}"
                                  Foreground="{StaticResource TextSecondaryBrush}" FontSize="10"
                                  Checked="OnCrosshairsToggled" Unchecked="OnCrosshairsToggled" />
                    </StackPanel>

                    ''' + axial_view + coronal_view + sagittal_view + '''
                </Grid>

                <!-- TAB 1: MEASUREMENTS -->
                <Grid Grid.Row="1" Visibility="{Binding RightPanelTabIndex, Converter={StaticResource IntToVisibilityConverter}, ConverterParameter=1}" Margin="4,8,4,4">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>
                    
                    <TextBlock Grid.Row="0" Text="CUSTOM MEASUREMENTS" FontSize="12" FontWeight="Bold" Foreground="White" Margin="4,0,0,8" />
                    <Border Grid.Row="1" Background="#FF1E222A" BorderBrush="{StaticResource BorderBrush}" BorderThickness="1" CornerRadius="4" Padding="8" Margin="0,0,0,8">
                        <TextBlock Text="Custom distance and angle measurements will appear here." Foreground="{StaticResource TextSecondaryBrush}" FontSize="11" FontStyle="Italic" TextWrapping="Wrap" />
                    </Border>

                    <TextBlock Grid.Row="2" Text="CEPHALOMETRY" FontSize="12" FontWeight="Bold" Foreground="White" Margin="4,8,0,8" />
                    <Border Grid.Row="3" Background="#FF1E222A" BorderBrush="{StaticResource BorderBrush}" BorderThickness="1" CornerRadius="4" Padding="8">
                        <TextBlock Text="Place landmarks, trace cephalometric planes, and compute angular and linear measurements." Foreground="{StaticResource TextSecondaryBrush}" FontSize="11" FontStyle="Italic" TextWrapping="Wrap" />
                    </Border>
                </Grid>

                <!-- TAB 2: SURGERY -->
                <Grid Grid.Row="1" Visibility="{Binding RightPanelTabIndex, Converter={StaticResource IntToVisibilityConverter}, ConverterParameter=2}" Margin="4,8,4,4">
                    ''' + overlay_text.replace('SurgicalMovementsOverlay', 'SurgeryTabContainer') + '''
                </Grid>
            </Grid>

            '''

text = text[:col4_start] + new_right_col + text[photogrammetry_start:]

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)

print("MainWindow.xaml Refactored successfully!")
