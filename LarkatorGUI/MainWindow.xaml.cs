﻿using FastMember;
using GongSolutions.Wpf.DragDrop;
using Larkator.Common;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Deployment.Application;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LarkatorGUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDropTarget
    {
        private const string DEV_STRING = "DEVELOPMENT";

        public ObservableCollection<SearchCriteria> ListSearches { get; } = new ObservableCollection<SearchCriteria>();
        public Collection<DinoViewModel> ListResults { get; } = new Collection<DinoViewModel>();
        public List<string> AllSpecies { get { return arkReaderWild.AllSpecies; } }

        public string ApplicationVersion
        {
            get
            {
                return appVersion;
            }
        }

        public string WindowTitle { get { return $"{Properties.Resources.ProgramName} {ApplicationVersion}"; } }
        public MapCalibration MapCalibration { get; private set; }
        public ImageSource MapImage { get; private set; }

        public bool IsLoading
        {
            get { return (bool)GetValue(IsLoadingProperty); }
            set { SetValue(IsLoadingProperty, value); }
        }

        public string StatusText
        {
            get { return (string)GetValue(StatusTextProperty); }
            set { SetValue(StatusTextProperty, value); }
        }

        public string StatusDetailText
        {
            get { return (string)GetValue(StatusDetailTextProperty); }
            set { SetValue(StatusDetailTextProperty, value); }
        }

        public SearchCriteria NewSearch
        {
            get { return (SearchCriteria)GetValue(NewSearchProperty); }
            set { SetValue(NewSearchProperty, value); }
        }

        public bool CreateSearchAvailable
        {
            get { return (bool)GetValue(CreateSearchAvailableProperty); }
            set { SetValue(CreateSearchAvailableProperty, value); }
        }

        public bool NewSearchActive
        {
            get { return (bool)GetValue(NewSearchActiveProperty); }
            set { SetValue(NewSearchActiveProperty, value); }
        }

        public bool ShowHunt
        {
            get { return (bool)GetValue(ShowHuntProperty); }
            set { SetValue(ShowHuntProperty, value); }
        }

        public bool ShowTames
        {
            get { return (bool)GetValue(ShowTamesProperty); }
            set { SetValue(ShowTamesProperty, value); }
        }

        public ImageSource ImageSource
        {
            get { return (ImageSource)GetValue(ImageSourceProperty); }
            set { SetValue(ImageSourceProperty, value); }
        }

        public string SearchText
        {
            get { return (string)GetValue(SearchTextProperty); }
            set { SetValue(SearchTextProperty, value); }
        }

        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register("SearchText", typeof(string), typeof(MainWindow), new PropertyMetadata(""));

        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register("ImageSource", typeof(ImageSource), typeof(MainWindow), new PropertyMetadata(null));

        public static readonly DependencyProperty ShowTamesProperty =
            DependencyProperty.Register("ShowTames", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public static readonly DependencyProperty ShowHuntProperty =
            DependencyProperty.Register("ShowHunt", typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        public static readonly DependencyProperty NewSearchActiveProperty =
            DependencyProperty.Register("NewSearchActive", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public static readonly DependencyProperty CreateSearchAvailableProperty =
            DependencyProperty.Register("CreateSearchAvailable", typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        public static readonly DependencyProperty NewSearchProperty =
            DependencyProperty.Register("NewSearch", typeof(SearchCriteria), typeof(MainWindow), new PropertyMetadata(null));

        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register("IsLoading", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register("StatusText", typeof(string), typeof(MainWindow), new PropertyMetadata(""));

        public static readonly DependencyProperty StatusDetailTextProperty =
            DependencyProperty.Register("StatusDetailText", typeof(string), typeof(MainWindow), new PropertyMetadata(""));


        ArkReader arkReaderWild;
        ArkReader arkReaderTamed;
        FileSystemWatcher fileWatcher;
        DispatcherTimer reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private List<MapCalibration> mapCalibrations;
        private readonly string appVersion;
        readonly List<bool?> nullableBoolValues = new List<bool?> { null, false, true };
        DateTime nameSearchTime;
        string nameSearchArg;
        bool nameSearchRunning;

        public MainWindow()
        {
            ValidateWindowPositionAndSize();

            arkReaderWild = new ArkReader(true);
            arkReaderTamed = new ArkReader(false);

            appVersion = CalculateApplicationVersion();

            LoadCalibrations();
            DiscoverCalibration();

            DataContext = this;

            InitializeComponent();

            devButtons.Visibility = (ApplicationVersion == DEV_STRING) ? Visibility.Visible : Visibility.Collapsed;
            Dispatcher.Invoke(async () =>
            {
                await Task.Yield();
                Properties.Settings.Default.MainWindowWidth = CalculateWidthFromHeight((int)Math.Round(Properties.Settings.Default.MainWindowHeight));
            }, DispatcherPriority.Loaded);

            LoadSavedSearches();
            EnsureOutputDirectory();
            SetupFileWatcher();
            CheckIfArkChanged(false);

            var cmdThrowExceptionAndExit = new RoutedCommand();
            cmdThrowExceptionAndExit.InputGestures.Add(new KeyGesture(Key.F2, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(cmdThrowExceptionAndExit, (o, e) => Dev_GenerateException_Click(null, null)));

            DependencyPropertyDescriptor.FromProperty(SearchTextProperty, typeof(MainWindow)).AddValueChanged(DataContext, (s,e) => TriggerNameSearch());
        }

        private static string CalculateApplicationVersion()
        {
            try
            {
                return ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
            }
            catch (InvalidDeploymentException)
            {
                return DEV_STRING;
            }
        }

        private void SetupFileWatcher()
        {
            if (fileWatcher != null) fileWatcher.EnableRaisingEvents = false;
            fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(Properties.Settings.Default.SaveFile));
            fileWatcher.Renamed += FileWatcher_Changed;
            fileWatcher.EnableRaisingEvents = true;
            reloadTimer.Interval = TimeSpan.FromMilliseconds(Properties.Settings.Default.ConvertDelay);
            reloadTimer.Tick += ReloadTimer_Tick;
        }

        private void LoadCalibrations()
        {
            mapCalibrations = JsonConvert.DeserializeObject<List<MapCalibration>>(Properties.Resources.calibrationsJson);
        }

        private void DiscoverCalibration()
        {
            var filename = Properties.Settings.Default.SaveFile;
            filename = Path.GetFileNameWithoutExtension(filename);
            var best = mapCalibrations.FirstOrDefault(cal => filename.StartsWith(cal.Filename));
            if (best == null)
            {
                StatusText = "Warning: Unable to determine map from filename - defaulting to The Island";
                MapCalibration = mapCalibrations.Single(cal => cal.Filename == "TheIsland");
            }
            else
            {
                MapCalibration = best;
            }

            var imgFilename = $"pack://application:,,,/imgs/map_{MapCalibration.Filename}.jpg";
            MapImage = (new ImageSourceConverter()).ConvertFromString(imgFilename) as ImageSource;
            if (image != null) image.Source = MapImage;
        }

        private void ValidateWindowPositionAndSize()
        {
            var settings = Properties.Settings.Default;

            if (settings.MainWindowLeft <= -10000 || settings.MainWindowTop <= -10000)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (settings.MainWindowWidth < 0 || settings.MainWindowHeight < 0)
            {
                settings.MainWindowWidth = (double)settings.Properties["MainWindowWidth"].DefaultValue;
                settings.MainWindowHeight = (double)settings.Properties["MainWindowHeight"].DefaultValue;
                settings.Save();
            }
        }

        private void EnsureOutputDirectory()
        {
            if (String.IsNullOrWhiteSpace(Properties.Settings.Default.OutputDir))
            {
                Properties.Settings.Default.OutputDir = Path.Combine(Path.GetTempPath(), Properties.Resources.ProgramName);
                if (!Directory.Exists(Properties.Settings.Default.OutputDir))
                {
                    Directory.CreateDirectory(Properties.Settings.Default.OutputDir);
                    Properties.Settings.Default.Save();
                }
            }
        }

        private void CheckIfArkChanged(bool reconvert = true)
        {
            var arkChanged = false;
            try
            {
                var lastArk = File.ReadAllText(Path.Combine(Properties.Settings.Default.OutputDir, Properties.Resources.LastArkFile));
                if (lastArk != Properties.Settings.Default.SaveFile) arkChanged = true;
            }
            catch
            {
                arkChanged = true;
            }

            if (arkChanged)
            {
                arkReaderTamed.ForceNextConversion = true;
                arkReaderWild.ForceNextConversion = true;

                if (reconvert)
                {
                    NotifyArkChanged();
                }
            }
        }

        private void NotifyArkChanged()
        {
            // Cause a fresh conversion of the new ark
            Dispatcher.Invoke(() => ReReadArk(force: true), DispatcherPriority.Background);

            // Ensure the file watcher is watching the right directory
            fileWatcher.Path = Path.GetDirectoryName(Properties.Settings.Default.SaveFile);
        }

        private void FileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (!String.Equals(e.FullPath, Properties.Settings.Default.SaveFile)) return;

            Dispatcher.Invoke(() =>
            {
                StatusText = "Detected change to saved ARK...";
                StatusDetailText = "...waiting";
            });

            // Cancel any existing timer to ensure we're not called multiple times
            if (reloadTimer.IsEnabled) reloadTimer.Stop();

            reloadTimer.Start();
        }

        private async void ReloadTimer_Tick(object sender, EventArgs e)
        {
            reloadTimer.Stop();
            await Dispatcher.InvokeAsync(() => ReReadArk(), DispatcherPriority.Background);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateArkToolsData();
            await ReReadArk();
        }

        private void Searches_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCurrentSearch();
        }

        private void RemoveSearch_Click(object sender, RoutedEventArgs e)
        {
            if (ShowTames) return;

            var button = sender as Button;
            if (button?.DataContext is SearchCriteria search) ListSearches.Remove(search);
            UpdateCurrentSearch();

            MarkSearchesChanged();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchText = "";

            TriggerNameSearch(true);
        }

        private void Search_Click(object sender, MouseButtonEventArgs e)
        {
            TriggerNameSearch(true);
        }

        private void SearchText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                TriggerNameSearch(true);
        }

        private void MapPin_Click(object sender, MouseButtonEventArgs ev)
        {
            if (sender is FrameworkElement e && e.DataContext is DinoViewModel dvm)
            {
                dvm.Highlight = !dvm.Highlight;
                if (dvm.Highlight) resultsList.ScrollIntoView(dvm);
            }
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // Handler to maintain window aspect ratio
            WindowAspectRatio.Register((Window)sender, CalculateWidthFromHeight);
        }

        private int CalculateWidthFromHeight(int height)
        {
            return (int)Math.Round(height
                - statusPanel.ActualHeight
                - 2 * SystemParameters.ResizeFrameHorizontalBorderHeight
                - SystemParameters.WindowCaptionHeight
                + leftPanel.ActualWidth + rightPanel.ActualWidth
                + 2 * SystemParameters.ResizeFrameVerticalBorderWidth);
        }

        private void CreateSearch_Click(object sender, RoutedEventArgs e)
        {
            NewSearch = new SearchCriteria();
            NewSearchActive = true;
            CreateSearchAvailable = false;

            speciesCombo.ItemsSource = arkReaderWild.AllSpecies;
            groupsCombo.ItemsSource = ListSearches.Select(sc => sc.Group).Distinct().OrderBy(g => g).ToArray();
        }

        private void Dev_Calibration_Click(object sender, MouseButtonEventArgs e)
        {
            var win = new CalibrationWindow(new Calibration { Bounds = new Bounds() });
            win.ShowDialog();
        }

        private void Dev_GenerateException_Click(object sender, MouseButtonEventArgs e)
        {
            throw new ApplicationException("Dummy unhandled exception");
        }

        private void Dev_RemoveSettings_Click(object sender, MouseButtonEventArgs e)
        {
            var result = MessageBox.Show(this, "Are you sure you wish to reset your options and force the application to exit?", "Unrecoverable action", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (result != MessageBoxResult.OK)
                return;

            Properties.Settings.Default.Reset();
            Properties.Settings.Default.Save();
            Environment.Exit(0);
        }

        private void Dev_DummyData_Click(object sender, MouseButtonEventArgs e)
        {
            ListResults.Clear();

            var dummyData = new Dino[] {
                new Dino { Location=new Position{ Lat=10,Lon=10 }, Type="Testificate", Name="10,10" },
                new Dino { Location=new Position{ Lat=90,Lon=10 }, Type="Testificate", Name="90,10" },
                new Dino { Location=new Position{ Lat=10,Lon=90 }, Type="Testificate", Name="10,90" },
                new Dino { Location=new Position{ Lat=90,Lon=90 }, Type="Testificate", Name="90,90" },
                new Dino { Location=new Position{ Lat=50,Lon=50 }, Type="Testificate", Name="50,50" },
            };

            var rnd = new Random();
            foreach (var result in dummyData)
            {
                result.Id = (ulong)rnd.Next();
                DinoViewModel vm = new DinoViewModel(result) { Color = Colors.Green };
                ListResults.Add(vm);
            }

            ((CollectionViewSource)Resources["OrderedResults"]).View.Refresh();
        }

        private async void SaveSearch_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(NewSearch.Species)) return;

            try
            {
                NewSearch.Order = ListSearches.Where(sc => sc.Group == NewSearch.Group).Max(sc => sc.Order) + 100;
            }
            catch (InvalidOperationException) // no entries for .Max - ignore
            { }

            IsLoading = true;
            try
            {
                StatusText = $"Loading {NewSearch.Species}...";
                await arkReaderWild.EnsureSpeciesIsLoaded(NewSearch.Species);
                StatusText = $"Ready";
            }
            finally
            {
                IsLoading = false;
            }

            ListSearches.Add(NewSearch);
            NewSearch = null;
            NewSearchActive = false;
            CreateSearchAvailable = true;

            MarkSearchesChanged();
        }

        private void CloseNewSearch_Click(object sender, MouseButtonEventArgs e)
        {
            NewSearchActive = false;
            CreateSearchAvailable = true;
        }

        private void ShowTames_Click(object sender, MouseButtonEventArgs e)
        {
            ShowTames = true;
            ShowHunt = false;
            NewSearchActive = false;
            CreateSearchAvailable = false;

            ShowTameSearches();
        }

        private void ShowTheHunt_Click(object sender, MouseButtonEventArgs e)
        {
            ShowTames = false;
            ShowHunt = true;
            NewSearchActive = false;
            CreateSearchAvailable = true;

            ShowWildSearches();
        }

        private void Settings_Click(object sender, MouseButtonEventArgs e)
        {
            var settings = new SettingsWindow();
            settings.ShowDialog();

            OnSettingsChanged();
        }

        private void AdjustableInteger_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var tb = (TextBlock)sender;
            var diff = Math.Sign(e.Delta) * Properties.Settings.Default.LevelStep;
            var bexpr = tb.GetBindingExpression(TextBlock.TextProperty);
            var accessor = TypeAccessor.Create(typeof(SearchCriteria));
            var value = (int?)accessor[bexpr.ResolvedSource, bexpr.ResolvedSourcePropertyName];
            if (value.HasValue)
            {
                value = value + diff;
                if (value < 0 || value > Properties.Settings.Default.MaxLevel) value = null;
            }
            else
            {
                value = (diff > 0) ? 0 : Properties.Settings.Default.MaxLevel;
            }

            accessor[bexpr.ResolvedSource, bexpr.ResolvedSourcePropertyName] = value;
            bexpr.UpdateTarget();

            if (null != searchesList.SelectedItem)
                UpdateCurrentSearch();

            MarkSearchesChanged();

            e.Handled = true;
        }

        private void AdjustableGender_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var im = (Image)sender;
            var diff = Math.Sign(e.Delta);
            var nOptions = nullableBoolValues.Count;
            var bexpr = im.GetBindingExpression(Image.SourceProperty);
            var accessor = TypeAccessor.Create(typeof(SearchCriteria));
            var value = (bool?)accessor[bexpr.ResolvedSource, bexpr.ResolvedSourcePropertyName];
            var index = nullableBoolValues.IndexOf(value);
            index = (index + diff + nOptions) % nOptions;
            value = nullableBoolValues[index];
            accessor[bexpr.ResolvedSource, bexpr.ResolvedSourcePropertyName] = value;
            bexpr.UpdateTarget();

            if (null != searchesList.SelectedItem)
                UpdateCurrentSearch();

            MarkSearchesChanged();

            e.Handled = true;
        }

        private void Result_MouseEnter(object sender, MouseEventArgs e)
        {
            var fe = sender as FrameworkElement;
            if (fe == null) return;
            var dino = fe.DataContext as DinoViewModel;
            if (dino == null) return;
            //dino.Highlight = true;
        }

        private void Result_MouseLeave(object sender, MouseEventArgs e)
        {
            var fe = sender as FrameworkElement;
            if (fe == null) return;
            var dino = fe.DataContext as DinoViewModel;
            if (dino == null) return;
            //dino.ClearValue(DinoViewModel.HighlightProperty);
        }

        private void ResultList_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e.Column.SortDirection == null)
                e.Column.SortDirection = ListSortDirection.Ascending;

            e.Handled = false;
        }

        private void MarkSearchesChanged()
        {
            SaveSearches();
        }

        private void SaveSearches()
        {
            Properties.Settings.Default.SavedSearches = JsonConvert.SerializeObject(ListSearches);
            Properties.Settings.Default.Save();
        }

        private void LoadSavedSearches()
        {
            if (!String.IsNullOrWhiteSpace(Properties.Settings.Default.SavedSearches))
            {
                Collection<SearchCriteria> searches;
                try
                {
                    searches = JsonConvert.DeserializeObject<Collection<SearchCriteria>>(Properties.Settings.Default.SavedSearches);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception reading saved searches: " + e.ToString());
                    return;
                }

                ListSearches.Clear();
                foreach (var search in searches)
                    ListSearches.Add(search);
            }
        }

        private async Task UpdateArkToolsData()
        {
            StatusText = "Updating ark-tools database";
            try
            {
                await ArkReader.ExecuteArkTools("update-data");
                StatusText = "Updated ark-tools database";
            }
            catch (Exception e)
            {
                StatusText = "Failed to update ark-tools database: " + e.Message;
            }
        }

        private async Task ReReadArk(bool force = false)
        {
            if (IsLoading) return;

            await PerformConversion(force);
            await LoadSearchSpecies();

            var currentSearch = searchesList.SelectedItems.Cast<SearchCriteria>().ToList();
            UpdateSearchResults(currentSearch);
        }

        private async Task LoadSearchSpecies()
        {
            IsLoading = true;
            try
            {
                var species = arkReaderWild.AllSpecies.Distinct();
                foreach (var speciesName in species)
                {
                    StatusText = $"Loading {speciesName}...";
                    await arkReaderWild.EnsureSpeciesIsLoaded(speciesName);
                }

                species = arkReaderTamed.AllSpecies.Distinct();
                foreach (var speciesName in species)
                {
                    StatusText = $"Loading {speciesName}...";
                    await arkReaderTamed.EnsureSpeciesIsLoaded(speciesName);
                }

                StatusText = "Ready";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void UpdateSearchResults(IList<SearchCriteria> searches)
        {
            if (searches == null || searches.Count == 0)
            {
                ListResults.Clear();
            }
            else
            {
                // Find dinos that match the given searches
                var found = new List<Dino>();
                var reader = ShowTames ? arkReaderTamed : arkReaderWild;
                foreach (var search in searches)
                {
                    if (String.IsNullOrWhiteSpace(search.Species))
                    {
                        foreach (var speciesDinos in reader.FoundDinos.Values)
                            found.AddRange(speciesDinos);
                    }
                    else
                    {
                        if (reader.FoundDinos.ContainsKey(search.Species))
                        {
                            var dinoList = reader.FoundDinos[search.Species];
                            found.AddRange(dinoList.Where(d => search.Matches(d)));
                        }
                    }
                }

                ListResults.Clear();
                foreach (var result in found)
                    ListResults.Add(result);
            }

            ((CollectionViewSource)Resources["OrderedResults"]).View.Refresh();

            TriggerNameSearch(true);
        }

        private async Task PerformConversion(bool force)
        {
            string arkDirName = Path.GetFileNameWithoutExtension(Properties.Settings.Default.SaveFile);

            IsLoading = true;
            try
            {
                StatusDetailText = "...converting";
                StatusText = "Processing saved ARK : Wild";
                await arkReaderWild.PerformConversion(force, arkDirName);
                StatusText = "Processing saved ARK : Tamed";
                await arkReaderTamed.PerformConversion(force, arkDirName);
                StatusText = "ARK processing completed";
                StatusDetailText = $"{arkReaderWild.NumberOfSpecies} wild and {arkReaderTamed.NumberOfSpecies} tame species located";

                // Write path to last ark into the output folder so we can check when we change ARKs
                File.WriteAllText(Path.Combine(Properties.Settings.Default.OutputDir, Properties.Resources.LastArkFile), Properties.Settings.Default.SaveFile);
            }
            catch (Exception ex)
            {
                StatusText = "ARK processing failed";
                StatusDetailText = "";
                MessageBox.Show(ex.Message, "ARK Tools Error", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void UpdateCurrentSearch()
        {
            var search = (SearchCriteria)searchesList.SelectedItem;
            var searches = new List<SearchCriteria>();
            if (search != null) searches.Add(search);
            UpdateSearchResults(searches);
        }

        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.TargetItem is SearchCriteria targetItem && dropInfo.Data is SearchCriteria sourceItem)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Move;
            }
        }

        void IDropTarget.Drop(IDropInfo dropInfo)
        {
            var sourceItem = (SearchCriteria)dropInfo.Data;
            var targetItem = (SearchCriteria)dropInfo.TargetItem;

            var ii = dropInfo.InsertIndex;
            var ip = dropInfo.InsertPosition;

            // Change source item's group
            sourceItem.Group = targetItem.Group;

            // Try to figure out the other item to insert between, or pick a boundary
            var options = ListSearches
                .Where(sc => sc != sourceItem)
                .Where(sc => sc.Group == targetItem.Group)
                .OrderBy(sc => sc.Order)
                .ToArray();

            var above = options.Where(sc => sc.Order < targetItem.Order).OrderByDescending(sc => sc.Order).FirstOrDefault();
            var below = options.Where(sc => sc.Order > targetItem.Order).OrderBy(sc => sc.Order).FirstOrDefault();

            var aboveOrder = (above == null) ? options.Min(sc => sc.Order) - 1 : above.Order;
            var belowOrder = (below == null) ? options.Max(sc => sc.Order) + 1 : below.Order;

            // Update the order to be mid-way between either above or below, based on drag insert position
            sourceItem.Order = (targetItem.Order + (ip.HasFlag(RelativeInsertPosition.AfterTargetItem) ? belowOrder : aboveOrder)) / 2;

            // Renumber the results
            var orderedSearches = ListSearches
                .OrderBy(sc => sc.Group)
                .ThenBy(sc => sc.Order)
                .ToArray();

            for (var i = 0; i < orderedSearches.Length; i++)
            {
                orderedSearches[i].Order = i;
            }

            // Force binding update
            CollectionViewSource.GetDefaultView(searchesList.ItemsSource).Refresh();

            // Save list
            MarkSearchesChanged();
        }

        private void ShowTameSearches()
        {
            SetupTamedSearches();
        }

        private void SetupTamedSearches()
        {
            var wildcard = new string[] { null };
            var speciesList = wildcard.Concat(arkReaderTamed.AllSpecies).ToList();
            var orderList = Enumerable.Range(0, speciesList.Count);
            var searches = speciesList.Zip(orderList, (species, order) => new SearchCriteria { Species = species, Order = order });

            ListSearches.Clear();
            foreach (var search in searches)
                ListSearches.Add(search);

            TriggerNameSearch(true);
        }

        private void ShowWildSearches()
        {
            LoadSavedSearches();
        }

        private void OnSettingsChanged()
        {
            DiscoverCalibration();
            EnsureOutputDirectory();
            CheckIfArkChanged();
            UpdateCurrentSearch();

            ForceFontSizeUpdate();

            reloadTimer.Interval = TimeSpan.FromMilliseconds(Properties.Settings.Default.ConvertDelay);
        }

        private void ForceFontSizeUpdate()
        {
            Dispatcher.Invoke(() => RefreshDataGridColumnWidths("GroupedSearchCriteria", searchesList), DispatcherPriority.ContextIdle);
            Dispatcher.Invoke(() => RefreshDataGridColumnWidths("OrderedResults", resultsList), DispatcherPriority.ContextIdle);
        }

        private void RefreshDataGridColumnWidths(string resourceName, DataGrid dataGrid)
        {
            var widths = dataGrid.Columns.Select(col => col.Width).ToArray();
            foreach (var col in dataGrid.Columns)
                col.Width = 0;

            ((CollectionViewSource)Resources[resourceName]).View.Refresh();
            dataGrid.UpdateLayout();

            foreach (var o in dataGrid.Columns.Zip(widths, (col, width) => new { col, width }))
                o.col.Width = o.width;
        }

        private void TriggerNameSearch(bool immediate=false)
        {
            nameSearchTime = DateTime.Now + TimeSpan.FromSeconds(immediate ? 0.01 : 0.5);
            nameSearchArg = SearchText;

            if (!nameSearchRunning)
            {
                nameSearchRunning = true;
                Dispatcher.Invoke(WaitForNameSearch, DispatcherPriority.Background);
            }
        }

        private async Task WaitForNameSearch()
        {
            while (nameSearchTime > DateTime.Now)
            {
                await Task.Delay(100);
            }

            nameSearchRunning = false;
            
            var searchText = nameSearchArg.Trim();

            if (String.IsNullOrWhiteSpace(searchText) || searchText.Length < 2)
            {
                foreach (var dvm in ListResults)
                    dvm.Highlight = false;
            }
            else
            {
                foreach (var dvm in ListResults)
                    dvm.Highlight = (dvm.Dino.Name != null) && dvm.Dino.Name.Contains(searchText);
            }
        }
    }
}
