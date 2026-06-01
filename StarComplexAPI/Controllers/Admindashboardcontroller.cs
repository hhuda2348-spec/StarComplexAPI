using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using train.Services;

namespace train
{
    // ══════════════════════════════════════════════════════════════
    //  نموذج العرض — الموظف المؤرشف
    // ══════════════════════════════════════════════════════════════
    public class ArchivedEmployeeItemView
    {
        public int ArchiveId { get; set; }
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string ArchivedAt { get; set; } = string.Empty;
        public int FinancialRecordsCount { get; set; }
        public List<ArchivedFinancialRecordDto> FinancialRecords { get; set; } = new();
        public string FinancialCountLabel => FinancialRecordsCount > 0
            ? $"💰 {FinancialRecordsCount} سجل مالي"
            : "لا توجد سجلات";
        public bool HasFinancialRecords => FinancialRecordsCount > 0;
    }

    // ══════════════════════════════════════════════════════════════
    //  ViewModel
    // ══════════════════════════════════════════════════════════════
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private bool _isLoading;
        private bool _isLoadingLists;
        private bool _hasError;
        private string _errorMessage = string.Empty;
        private string _employeeName = string.Empty;
        private string _employeeTitle = string.Empty;
        private string _totalResidents = "—";
        private string _residentsSub = string.Empty;
        private string _occupiedUnits = "—";
        private string _occupancyRate = string.Empty;
        private string _pendingVisits = "—";
        private string _visitsSub = string.Empty;
        private string _openMaintenance = "—";
        private string _maintenanceSub = string.Empty;
        private string _totalRevenue = "—";
        private string _monthlyRevenue = "—";
        private string _currentMonthLabel = string.Empty;
        private string _topService = "—";
        private string _topServiceCount = string.Empty;
        private string _employeeCountLabel = "جارٍ التحميل...";
        private string _blacklistCountLabel = "جارٍ التحميل...";
        private string _archiveCountLabel = "جارٍ التحميل...";
        private string _searchText = string.Empty;

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public bool IsLoadingLists
        {
            get => _isLoadingLists;
            set { _isLoadingLists = value; OnPropertyChanged(); }
        }

        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public string EmployeeName
        {
            get => _employeeName;
            set { _employeeName = value; OnPropertyChanged(); }
        }

        public string EmployeeTitle
        {
            get => _employeeTitle;
            set { _employeeTitle = value; OnPropertyChanged(); }
        }

        public string TotalResidents
        {
            get => _totalResidents;
            set { _totalResidents = value; OnPropertyChanged(); }
        }

        public string ResidentsSub
        {
            get => _residentsSub;
            set { _residentsSub = value; OnPropertyChanged(); }
        }

        public string OccupiedUnits
        {
            get => _occupiedUnits;
            set { _occupiedUnits = value; OnPropertyChanged(); }
        }

        public string OccupancyRate
        {
            get => _occupancyRate;
            set { _occupancyRate = value; OnPropertyChanged(); }
        }

        public string PendingVisits
        {
            get => _pendingVisits;
            set { _pendingVisits = value; OnPropertyChanged(); }
        }

        public string VisitsSub
        {
            get => _visitsSub;
            set { _visitsSub = value; OnPropertyChanged(); }
        }

        public string OpenMaintenance
        {
            get => _openMaintenance;
            set { _openMaintenance = value; OnPropertyChanged(); }
        }

        public string MaintenanceSub
        {
            get => _maintenanceSub;
            set { _maintenanceSub = value; OnPropertyChanged(); }
        }

        public string TotalRevenue
        {
            get => _totalRevenue;
            set { _totalRevenue = value; OnPropertyChanged(); }
        }

        public string MonthlyRevenue
        {
            get => _monthlyRevenue;
            set { _monthlyRevenue = value; OnPropertyChanged(); }
        }

        public string CurrentMonthLabel
        {
            get => _currentMonthLabel;
            set { _currentMonthLabel = value; OnPropertyChanged(); }
        }

        public string TopService
        {
            get => _topService;
            set { _topService = value; OnPropertyChanged(); }
        }

        public string TopServiceCount
        {
            get => _topServiceCount;
            set { _topServiceCount = value; OnPropertyChanged(); }
        }

        public string EmployeeCountLabel
        {
            get => _employeeCountLabel;
            set { _employeeCountLabel = value; OnPropertyChanged(); }
        }

        public string BlacklistCountLabel
        {
            get => _blacklistCountLabel;
            set { _blacklistCountLabel = value; OnPropertyChanged(); }
        }

        public string ArchiveCountLabel
        {
            get => _archiveCountLabel;
            set { _archiveCountLabel = value; OnPropertyChanged(); }
        }

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<EmployeeItemView> EmployeeItems { get; set; } = new();
        public ObservableCollection<BlacklistItemView> BlacklistItems { get; set; } = new();
        public ObservableCollection<ArchivedEmployeeItemView> ArchivedEmployeeItems { get; set; } = new();

        public ObservableCollection<EmployeeItemView> FilteredEmployeeItems { get; set; } = new();
        public ObservableCollection<BlacklistItemView> FilteredBlacklistItems { get; set; } = new();
        public ObservableCollection<ArchivedEmployeeItemView> FilteredArchivedItems { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ══════════════════════════════════════════════════════════════
    //  AdminDashboardPage — Code-Behind
    // ══════════════════════════════════════════════════════════════
    public partial class AdminDashboardPage : ContentPage
    {
        private readonly DashboardService _service;
        private readonly DashboardViewModel _vm = new();

        private int _currentEmployeeId;
        private bool _sidebarVisible;

        private readonly Color _activeColor = Color.FromArgb("#A61D33");
        private readonly Color _inactiveColor = Color.FromArgb("#8A8A8A");

        private int _chartResidents;
        private int _chartOccupied;
        private int _chartPending;
        private int _chartMaintenance;

        private List<ServiceItemDto> _availableServices = new();
        private List<UnitItemDto> _availableUnits = new();

        public AdminDashboardPage(DashboardService service, int employeeId = 1)
        {
            InitializeComponent();
            _service = service;
            _currentEmployeeId = employeeId;
            BindingContext = _vm;
            UpdateTabUI("Dashboard");
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _currentEmployeeId = Preferences.Get("employee_id", _currentEmployeeId);
            UpdateTabUI("Dashboard");
            await LoadDashboardAsync();
        }

        // ══════════════════════════════════════════════════════════
        //  تحميل البيانات — مقسّم لمرحلتين
        // ══════════════════════════════════════════════════════════
        private async Task LoadDashboardAsync(bool isRetry = false)
        {
            _vm.IsLoading = true;
            _vm.HasError = false;
            _vm.ErrorMessage = string.Empty;

            try
            {
                var (dashboard, services, units) =
                    await _service.GetAllInitialDataAsync(_currentEmployeeId);

                _availableServices = services;
                _availableUnits = units;

                var kpi = dashboard.Kpi;
                _vm.EmployeeName = kpi.EmployeeName;
                _vm.EmployeeTitle = kpi.EmployeeTitle;
                _vm.TotalResidents = kpi.TotalResidents.ToString("N0");
                _vm.ResidentsSub = kpi.ResidentsSub;
                _vm.OccupiedUnits = $"{kpi.OccupiedUnits} / {kpi.TotalUnits}";
                _vm.OccupancyRate = kpi.OccupancyRate;
                _vm.PendingVisits = kpi.PendingVisits.ToString();
                _vm.VisitsSub = kpi.VisitsSub;
                _vm.OpenMaintenance = kpi.OpenMaintenance.ToString();
                _vm.MaintenanceSub = kpi.MaintenanceSub;
                _vm.TotalRevenue = kpi.TotalRevenue;
                _vm.MonthlyRevenue = kpi.MonthlyRevenue;
                _vm.CurrentMonthLabel = kpi.CurrentMonthLabel;
                _vm.TopService = kpi.TopService;
                _vm.TopServiceCount = kpi.TopServiceCount;

                _chartResidents = kpi.TotalResidents;
                _chartOccupied = kpi.OccupiedUnits;
                _chartPending = kpi.PendingVisits;
                _chartMaintenance = kpi.OpenMaintenance;

                StatsChart.Drawable = new StatsChartDrawable(
                    _chartResidents, _chartOccupied, _chartPending, _chartMaintenance);
                StatsChart.Invalidate();

                _vm.IsLoading = false;

                _vm.IsLoadingLists = true;
                _vm.EmployeeCountLabel = "جارٍ تحميل الموظفين...";
                _vm.BlacklistCountLabel = "جارٍ تحميل القائمة...";
                _vm.ArchiveCountLabel = "جارٍ تحميل الأرشيف...";

                await Task.WhenAll(
                    SafeLoad(LoadEmployeesAsync, "موظفي الأمن"),
                    SafeLoad(LoadBlacklistAsync, "القائمة السوداء"),
                    SafeLoad(LoadArchivedEmployeesAsync, "الأرشيف")
                );
            }
            catch (HttpRequestException httpEx)
            {
                _vm.HasError = true;
                _vm.ErrorMessage = $"تعذّر الاتصال بالخادم.\n{httpEx.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ HTTP Error: {httpEx.Message}");

                if (!isRetry)
                {
                    await Task.Delay(2000);
                    await LoadDashboardAsync(isRetry: true);
                    return;
                }

                await DisplayAlert("خطأ في الاتصال",
                    "تعذّر الوصول إلى الخادم.\nتأكد من اتصالك بالإنترنت ثم اضغط إعادة المحاولة.",
                    "حسناً");
            }
            catch (TaskCanceledException)
            {
                _vm.HasError = true;
                _vm.ErrorMessage = "انتهت مهلة الاتصال. تحقق من الإنترنت.";
                await DisplayAlert("انتهت المهلة",
                    "استغرق الطلب وقتاً طويلاً. حاول مجدداً.", "حسناً");
            }
            catch (Exception ex)
            {
                _vm.HasError = true;
                _vm.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"❌ Dashboard Error: {ex}");
                await DisplayAlert("خطأ غير متوقع", ex.Message, "إغلاق");
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.IsLoadingLists = false;
            }
        }

        private static async Task SafeLoad(Func<Task> loader, string sectionName)
        {
            try { await loader(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ فشل تحميل {sectionName}: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════
        //  تحميل الموظفين
        // ══════════════════════════════════════════════════════════
        private async Task LoadEmployeesAsync()
        {
            var list = await _service.GetEmployeesAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _vm.EmployeeItems.Clear();
                _vm.FilteredEmployeeItems.Clear();

                foreach (var emp in list)
                {
                    var item = new EmployeeItemView
                    {
                        EmployeeId = emp.EmployeeId,
                        EmployeeCode = emp.EmployeeCode ?? "—",
                        FullName = emp.FullName?.Trim() ?? "—",
                        JobTitle = emp.JobTitle ?? "—",
                        PhoneNumber = emp.PhoneNumber ?? "—",
                        IsCurrentUser = emp.EmployeeId == _currentEmployeeId
                    };
                    _vm.EmployeeItems.Add(item);
                    _vm.FilteredEmployeeItems.Add(item);
                }

                _vm.EmployeeCountLabel = $"إجمالي الموظفين: {_vm.EmployeeItems.Count}";
            });
        }

        // ══════════════════════════════════════════════════════════
        //  تحميل القائمة السوداء
        // ══════════════════════════════════════════════════════════
        private async Task LoadBlacklistAsync()
        {
            var list = await _service.GetBlacklistAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _vm.BlacklistItems.Clear();
                _vm.FilteredBlacklistItems.Clear();

                foreach (var item in list)
                {
                    var view = new BlacklistItemView
                    {
                        BlacklistId = item.BlacklistId,
                        PersonName = item.PersonName ?? "—",
                        EmployeeId = item.EmployeeId,
                        AddedByName = item.AddedByName ?? "—",
                        Reason = item.Reason ?? string.Empty,
                        AddedDate = item.AddedDate
                    };
                    _vm.BlacklistItems.Add(view);
                    _vm.FilteredBlacklistItems.Add(view);
                }

                _vm.BlacklistCountLabel = $"إجمالي السجلات: {_vm.BlacklistItems.Count}";
            });
        }

        // ══════════════════════════════════════════════════════════
        //  تحميل الأرشيف
        // ══════════════════════════════════════════════════════════
        private async Task LoadArchivedEmployeesAsync()
        {
            var list = await _service.GetArchivedEmployeesAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _vm.ArchivedEmployeeItems.Clear();
                _vm.FilteredArchivedItems.Clear();

                foreach (var item in list)
                {
                    var view = new ArchivedEmployeeItemView
                    {
                        ArchiveId = item.ArchiveId,
                        EmployeeId = item.EmployeeId,
                        FullName = item.FullName?.Trim() ?? "—",
                        JobTitle = item.JobTitle ?? "—",
                        PhoneNumber = item.PhoneNumber ?? "—",
                        ArchivedAt = item.ArchivedAt.ToString("yyyy/MM/dd"),
                        FinancialRecordsCount = item.FinancialRecordsCount,
                        FinancialRecords = item.FinancialRecords
                    };
                    _vm.ArchivedEmployeeItems.Add(view);
                    _vm.FilteredArchivedItems.Add(view);
                }

                _vm.ArchiveCountLabel = $"إجمالي المؤرشفين: {_vm.ArchivedEmployeeItems.Count}";
            });
        }

        // ══════════════════════════════════════════════════════════
        //  البحث
        // ══════════════════════════════════════════════════════════
        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
            => ApplySearch((e.NewTextValue ?? string.Empty).Trim().ToLower());

        private void ApplySearch(string query)
        {
            bool empty = string.IsNullOrEmpty(query);

            var filteredEmps = empty
                ? _vm.EmployeeItems
                : (IEnumerable<EmployeeItemView>)_vm.EmployeeItems.Where(e =>
                    (e.FullName?.ToLower().Contains(query) ?? false) ||
                    (e.EmployeeCode?.ToLower().Contains(query) ?? false) ||
                    (e.JobTitle?.ToLower().Contains(query) ?? false) ||
                    (e.PhoneNumber?.ToLower().Contains(query) ?? false) ||
                    e.EmployeeId.ToString().Contains(query));

            _vm.FilteredEmployeeItems.Clear();
            foreach (var i in filteredEmps) _vm.FilteredEmployeeItems.Add(i);

            var filteredBlack = empty
                ? _vm.BlacklistItems
                : (IEnumerable<BlacklistItemView>)_vm.BlacklistItems.Where(b =>
                    (b.PersonName?.ToLower().Contains(query) ?? false) ||
                    (b.Reason?.ToLower().Contains(query) ?? false) ||
                    (b.AddedByName?.ToLower().Contains(query) ?? false) ||
                    b.AddedDate.ToString("yyyy/MM/dd").Contains(query));

            _vm.FilteredBlacklistItems.Clear();
            foreach (var i in filteredBlack) _vm.FilteredBlacklistItems.Add(i);

            var filteredArchive = empty
                ? _vm.ArchivedEmployeeItems
                : (IEnumerable<ArchivedEmployeeItemView>)_vm.ArchivedEmployeeItems.Where(a =>
                    (a.FullName?.ToLower().Contains(query) ?? false) ||
                    (a.JobTitle?.ToLower().Contains(query) ?? false) ||
                    (a.PhoneNumber?.ToLower().Contains(query) ?? false) ||
                    a.ArchivedAt.Contains(query) ||
                    a.EmployeeId.ToString().Contains(query));

            _vm.FilteredArchivedItems.Clear();
            foreach (var i in filteredArchive) _vm.FilteredArchivedItems.Add(i);

            _vm.EmployeeCountLabel = empty
                ? $"إجمالي الموظفين: {_vm.EmployeeItems.Count}"
                : $"نتائج البحث: {_vm.FilteredEmployeeItems.Count} من {_vm.EmployeeItems.Count}";

            _vm.BlacklistCountLabel = empty
                ? $"إجمالي السجلات: {_vm.BlacklistItems.Count}"
                : $"نتائج البحث: {_vm.FilteredBlacklistItems.Count} من {_vm.BlacklistItems.Count}";

            _vm.ArchiveCountLabel = empty
                ? $"إجمالي المؤرشفين: {_vm.ArchivedEmployeeItems.Count}"
                : $"نتائج البحث: {_vm.FilteredArchivedItems.Count} من {_vm.ArchivedEmployeeItems.Count}";
        }

        private void OnClearSearchClicked(object sender, EventArgs e)
        {
            SearchEntry.Text = string.Empty;
            ApplySearch(string.Empty);
        }

        private async void OnRetryClicked(object sender, EventArgs e)
            => await LoadDashboardAsync();

        // ══════════════════════════════════════════════════════════
        //  Sidebar
        // ══════════════════════════════════════════════════════════
        private async void OnToggleSidebarClicked(object sender, EventArgs e)
        {
            _sidebarVisible = !_sidebarVisible;
            if (_sidebarVisible)
            {
                SidebarBorder.IsVisible = true;
                SidebarBorder.TranslationX = -220;
                await SidebarBorder.TranslateTo(0, 0, 220, Easing.CubicOut);
            }
            else
            {
                await SidebarBorder.TranslateTo(-220, 0, 180, Easing.CubicIn);
                SidebarBorder.IsVisible = false;
            }
        }

        private async void CloseSidebar()
        {
            if (!_sidebarVisible) return;
            _sidebarVisible = false;
            await SidebarBorder.TranslateTo(-220, 0, 180, Easing.CubicIn);
            SidebarBorder.IsVisible = false;
        }

        // ══════════════════════════════════════════════════════════
        //  Bottom Tab Bar
        // ══════════════════════════════════════════════════════════
        private void UpdateTabUI(string activeTab)
        {
            Label[] icons = { IconDashboard, IconResidents, IconVisits, IconFinancials, IconMaintenance };
            Label[] texts = { TextDashboard, TextResidents, TextVisits, TextFinancials, TextMaintenance };

            foreach (var (icon, text) in icons.Zip(texts))
            {
                icon.TextColor = text.TextColor = _inactiveColor;
                text.FontAttributes = FontAttributes.None;
            }

            (Label icon, Label text) active = activeTab switch
            {
                "Residents" => (IconResidents, TextResidents),
                "Visits" => (IconVisits, TextVisits),
                "Financials" => (IconFinancials, TextFinancials),
                "Maintenance" => (IconMaintenance, TextMaintenance),
                _ => (IconDashboard, TextDashboard)
            };
            active.icon.TextColor = active.text.TextColor = _activeColor;
            active.text.FontAttributes = FontAttributes.Bold;
        }

        // ══════════════════════════════════════════════════════════
        //  Popup مخصص
        // ══════════════════════════════════════════════════════════
        private async Task<bool> ShowPopupAsync(View popupContent, double widthRequest = 420)
        {
            var popup = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(18) },
                StrokeThickness = 0,
                BackgroundColor = Colors.White,
                WidthRequest = widthRequest,
                MaximumHeightRequest = 680,
                Shadow = new Shadow
                {
                    Brush = new SolidColorBrush(Color.FromArgb("#000000")),
                    Opacity = 0.18f,
                    Radius = 24,
                    Offset = new Point(0, 8)
                },
                Content = new ScrollView { Content = popupContent },
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Opacity = 0,
                TranslationY = 30
            };

            var overlay = new Grid
            {
                BackgroundColor = Color.FromArgb("#80000000"),
                Children = { popup }
            };

            var originalContent = this.Content;
            var rootGrid = new Grid();
            rootGrid.Children.Add(originalContent);
            rootGrid.Children.Add(overlay);
            this.Content = rootGrid;

            await Task.WhenAll(
                popup.FadeTo(1, 220, Easing.CubicOut),
                popup.TranslateTo(0, 0, 220, Easing.CubicOut)
            );

            var tcs = (TaskCompletionSource<bool>)popupContent.BindingContext!;
            bool result = await tcs.Task;

            await Task.WhenAll(
                popup.FadeTo(0, 160, Easing.CubicIn),
                popup.TranslateTo(0, 20, 160, Easing.CubicIn)
            );

            rootGrid.Children.Remove(originalContent);
            this.Content = originalContent;
            return result;
        }

        // ══════════════════════════════════════════════════════════
        //  إضافة موظف
        // ══════════════════════════════════════════════════════════
        private async void OnAddEmployeeClicked(object sender, EventArgs e)
        {
            var entryCode = MakeEntry("مثال: EMP-001");
            var entryFirst = MakeEntry("الاسم الأول *");
            var entrySecond = MakeEntry("الاسم الثاني (اختياري)");
            var entryThird = MakeEntry("الاسم الثالث (اختياري)");
            var entryJob = MakeEntry("مثال: ضابط أمن");
            var entryPhone = MakeEntry("مثال: 07xxxxxxxx", Keyboard.Telephone);

            var tcs = new TaskCompletionSource<bool>();
            var btnAdd = MakeButton("✅  إضافة الموظف", "#6B1520", Colors.White);
            var btnCancel = MakeButton("إلغاء", "#EEEBE8", Color.FromArgb("#555555"));

            btnAdd.Clicked += (_, _) => tcs.TrySetResult(true);
            btnCancel.Clicked += (_, _) => tcs.TrySetResult(false);

            var namesGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 10,
                Children =
                {
                    FieldRow("الاسم الثاني", entrySecond),
                    SetCol(FieldRow("الاسم الثالث", entryThird), 1)
                }
            };

            var btnsGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 10,
                Children = { btnCancel, SetCol(btnAdd, 1) }
            };

            var form = new VerticalStackLayout
            {
                Spacing = 14,
                Padding = new Thickness(24, 20),
                FlowDirection = FlowDirection.RightToLeft,
                BindingContext = tcs,
                Children =
                {
                    SectionTitle("🛡️  إضافة موظف أمن"),
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E8E4DF") },
                    FieldRow("رمز الموظف (employee_code)", entryCode,  required: true),
                    FieldRow("الاسم الأول (first_name)",   entryFirst, required: true),
                    namesGrid,
                    FieldRow("المسمى الوظيفي (job_title)", entryJob),
                    FieldRow("رقم الهاتف (phone_number)",  entryPhone),
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E8E4DF"),
                                  Margin = new Thickness(0, 4) },
                    btnsGrid
                }
            };

            bool confirmed = await ShowPopupAsync(form, 460);
            if (!confirmed) return;

            var code = entryCode.Text?.Trim();
            var firstName = entryFirst.Text?.Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(firstName))
            {
                await DisplayAlert("تنبيه", "رمز الموظف والاسم الأول مطلوبان.", "موافق");
                return;
            }

            try
            {
                var request = new AddEmployeeRequest
                {
                    EmployeeCode = code,
                    FirstName = firstName,
                    SecondName = NullIfEmpty(entrySecond.Text),
                    ThirdName = NullIfEmpty(entryThird.Text),
                    JobTitle = NullIfEmpty(entryJob.Text),
                    PhoneNumber = NullIfEmpty(entryPhone.Text)
                };

                var result = await _service.AddEmployeeAsync(request);
                await DisplayAlert("✅ تم",
                    $"{result.Message}\nالاسم: {result.FullName}\nالرمز: {result.EmployeeCode}",
                    "موافق");
                await LoadEmployeesAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", $"فشل في الإضافة:\n{ex.Message}", "إغلاق");
            }
        }

        // ══════════════════════════════════════════════════════════
        //  حذف موظف — رسالة التأكيد تعرض نوع الأرشفة المتوقعة
        // ══════════════════════════════════════════════════════════
        private async void OnDeleteEmployeeClicked(object sender, EventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.CommandParameter is not int id) return;

            if (id == _currentEmployeeId)
            {
                await DisplayAlert("⚠️ غير مسموح",
                    "لا يمكنك حذف حسابك الخاص.\nيرجى التواصل مع المسؤول.", "موافق");
                return;
            }

            var emp = _vm.EmployeeItems.FirstOrDefault(x => x.EmployeeId == id);
            bool confirm = await DisplayAlert("تأكيد الحذف",
                $"هل تريد حذف الموظف '{emp?.FullName ?? id.ToString()}'؟\n" +
                "• إداري  → تُحفظ سجلاته المالية في الأرشيف\n" +
                "• أمن    → تُحفظ سجلات بلاغاته في الأرشيف",
                "حذف وأرشفة", "إلغاء");
            if (!confirm) return;

            try
            {
                var result = await _service.DeleteEmployeeAsync(id, _currentEmployeeId);

                // ✅ بناء تفاصيل الرسالة حسب نوع الموظف المُرجع من الخادم
                string details;
                if (result.EmployeeTypeArchived == "اداري" && result.FinancialRecordsKept > 0)
                    details = $"\nنوع الموظف: إداري\nتم الاحتفاظ بـ {result.FinancialRecordsKept} سجل مالي في الأرشيف.";
                else if (result.EmployeeTypeArchived == "امن" && result.SecurityLogsKept > 0)
                    details = $"\nنوع الموظف: أمن\nتم الاحتفاظ بـ {result.SecurityLogsKept} سجل بلاغ في الأرشيف.";
                else if (!string.IsNullOrEmpty(result.EmployeeTypeArchived))
                    details = $"\nنوع الموظف: {result.EmployeeTypeArchived}\nلا توجد سجلات مرتبطة.";
                else
                    details = string.Empty;

                await DisplayAlert("✅ تم", $"{result.Message}{details}", "موافق");

                await Task.WhenAll(LoadEmployeesAsync(), LoadArchivedEmployeesAsync());
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", $"فشل في الحذف:\n{ex.Message}", "إغلاق");
            }
        }

        // ══════════════════════════════════════════════════════════
        //  إضافة للقائمة السوداء
        // ══════════════════════════════════════════════════════════
        private async void OnAddBlacklistClicked(object sender, EventArgs e)
        {
            var entryName = MakeEntry("اسم الشخص المراد إضافته *");
            var entryReason = MakeEntry("سبب الإدراج (اختياري)");

            var tcs = new TaskCompletionSource<bool>();
            var btnConfirm = MakeButton("🚫  إضافة للقائمة", "#6B1520", Colors.White);
            var btnCancel = MakeButton("إلغاء", "#EEEBE8", Color.FromArgb("#555555"));

            btnConfirm.Clicked += (_, _) => tcs.TrySetResult(true);
            btnCancel.Clicked += (_, _) => tcs.TrySetResult(false);

            var btnsGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 10,
                Children = { btnCancel, SetCol(btnConfirm, 1) }
            };

            var dateInfo = new Border
            {
                BackgroundColor = Color.FromArgb("#FFF8E7"),
                StrokeThickness = 1,
                Stroke = new SolidColorBrush(Color.FromArgb("#D4A017")),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                { CornerRadius = new CornerRadius(8) },
                Padding = new Thickness(12, 8),
                Content = new Label
                {
                    Text = $"📅 تاريخ الإضافة: {DateTime.Now:yyyy/MM/dd} (يُضبط تلقائياً)",
                    FontSize = 11,
                    TextColor = Color.FromArgb("#8B6914")
                }
            };

            var form = new VerticalStackLayout
            {
                Spacing = 16,
                Padding = new Thickness(28),
                FlowDirection = FlowDirection.RightToLeft,
                BindingContext = tcs,
                Children =
                {
                    new Label { Text = "🚫", FontSize = 36, HorizontalOptions = LayoutOptions.Center },
                    new Label
                    {
                        Text = "إضافة إلى القائمة السوداء",
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#6B1520"),
                        HorizontalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = "أدخل بيانات الشخص المراد حظره من دخول المجمع",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#888888"),
                        HorizontalOptions = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, -6, 0, 0)
                    },
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E8E4DF") },
                    FieldRow("اسم الشخص (person_name)", entryName,   required: true),
                    FieldRow("سبب الحظر (reason)",       entryReason),
                    dateInfo,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E8E4DF") },
                    btnsGrid
                }
            };

            bool confirmed = await ShowPopupAsync(form, 420);
            if (!confirmed) return;

            var name = entryName.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await DisplayAlert("تنبيه", "يرجى إدخال اسم الشخص.", "موافق");
                return;
            }

            try
            {
                var request = new AddBlacklistRequest
                {
                    PersonName = name,
                    EmployeeId = _currentEmployeeId,
                    Reason = NullIfEmpty(entryReason.Text)
                };
                var result = await _service.AddToBlacklistAsync(request);
                await DisplayAlert("✅ تم", result.Message, "موافق");
                await LoadBlacklistAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", $"فشل في الإضافة:\n{ex.Message}", "إغلاق");
            }
        }

        // ══════════════════════════════════════════════════════════
        //  حذف من القائمة السوداء
        // ══════════════════════════════════════════════════════════
        private async void OnDeleteBlacklistClicked(object sender, EventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.CommandParameter is not int id) return;

            bool confirm = await DisplayAlert("تأكيد الحذف",
                "هل تريد حذف هذا السجل من القائمة السوداء؟", "حذف", "إلغاء");
            if (!confirm) return;

            try
            {
                var result = await _service.RemoveFromBlacklistAsync(id);
                await DisplayAlert("✅ تم", result.Message, "موافق");
                await LoadBlacklistAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", $"فشل في الحذف:\n{ex.Message}", "إغلاق");
            }
        }

        // ══════════════════════════════════════════════════════════
        //  Navigation
        // ══════════════════════════════════════════════════════════
        private void OnNavDashboardTapped(object sender, TappedEventArgs e)
        { UpdateTabUI("Dashboard"); CloseSidebar(); }

        private async void OnNavResidentsTapped(object sender, TappedEventArgs e)
        { UpdateTabUI("Residents"); CloseSidebar(); await Shell.Current.GoToAsync("ResidentsManagementPage"); }

        private async void OnNavVisitsTapped(object sender, TappedEventArgs e)
        { UpdateTabUI("Visits"); CloseSidebar(); await Shell.Current.GoToAsync("VisitsRequestsPage"); }

        private async void OnNavFinancialsTapped(object sender, EventArgs e)
        { UpdateTabUI("Financials"); CloseSidebar(); await Shell.Current.GoToAsync("FinancialsPage"); }

        private async void OnNavMaintenanceTapped(object sender, TappedEventArgs e)
        { UpdateTabUI("Maintenance"); CloseSidebar(); await Shell.Current.GoToAsync("MaintenanceRequestsPage"); }

        private async void OnNavProfileTapped(object sender, TappedEventArgs e)
        { CloseSidebar(); await Shell.Current.GoToAsync("EmployeeProfilePage"); }

        // ══════════════════════════════════════════════════════════
        //  UI Helpers
        // ══════════════════════════════════════════════════════════
        private static Entry MakeEntry(string placeholder, Keyboard? keyboard = null) => new()
        {
            Placeholder = placeholder,
            FlowDirection = FlowDirection.RightToLeft,
            BackgroundColor = Color.FromArgb("#F7F5F2"),
            TextColor = Color.FromArgb("#222222"),
            PlaceholderColor = Color.FromArgb("#AAAAAA"),
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 0),
            HeightRequest = 44,
            Keyboard = keyboard ?? Keyboard.Default
        };

        private static Button MakeButton(string text, string bgHex, Color textColor) => new()
        {
            Text = text,
            BackgroundColor = Color.FromArgb(bgHex),
            TextColor = textColor,
            CornerRadius = 10,
            HeightRequest = 46,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold
        };

        private static VerticalStackLayout FieldRow(string labelText, View field, bool required = false)
        {
            var lbl = new Label
            {
                Text = required ? $"{labelText} *" : labelText,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(required ? "#6B1520" : "#555555"),
                FlowDirection = FlowDirection.RightToLeft
            };
            return new VerticalStackLayout { Spacing = 2, Children = { lbl, field } };
        }

        private static Label SectionTitle(string text) => new()
        {
            Text = text,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#6B1520"),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };

        private static T SetCol<T>(T view, int col) where T : View
        {
            Grid.SetColumn(view, col);
            return view;
        }

        private static string? NullIfEmpty(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    // ══════════════════════════════════════════════════════════════
    //  نماذج عرض البيانات
    // ══════════════════════════════════════════════════════════════
    public class EmployeeItemView
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public bool IsCurrentUser { get; set; }
        public bool CanDelete => !IsCurrentUser;
        public Color DeleteButtonColor => IsCurrentUser
            ? Color.FromArgb("#CCCCCC")
            : Color.FromArgb("#E74C3C");
    }

    public class BlacklistItemView
    {
        public int BlacklistId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string AddedByName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime AddedDate { get; set; } = DateTime.Now;
        public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
    }

    // ══════════════════════════════════════════════════════════════
    //  رسم الإحصائيات (Bar Chart)
    // ══════════════════════════════════════════════════════════════
    public class StatsChartDrawable : IDrawable
    {
        private readonly int _residents;
        private readonly int _occupied;
        private readonly int _pending;
        private readonly int _maintenance;

        private static readonly Color[] BarColors =
        {
            Color.FromArgb("#6B1520"),
            Color.FromArgb("#D4A017"),
            Color.FromArgb("#E67E22"),
            Color.FromArgb("#D63031")
        };

        private static readonly string[] Labels =
            { "السكان", "المشغولة", "الزيارات", "الصيانة" };

        public StatsChartDrawable(int residents, int occupied, int pending, int maintenance)
        {
            _residents = residents;
            _occupied = occupied;
            _pending = pending;
            _maintenance = maintenance;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var values = new[] { _residents, _occupied, _pending, _maintenance };
            int maxVal = Math.Max(values.Max(), 1);

            float width = dirtyRect.Width;
            float height = dirtyRect.Height;
            float padBottom = 30f;
            float padTop = 20f;
            float chartHeight = height - padBottom - padTop;
            float totalBar = width / values.Length;
            float barWidth = totalBar * 0.5f;
            float gap = totalBar * 0.25f;

            canvas.StrokeColor = Color.FromArgb("#E8E4DF");
            canvas.StrokeSize = 1;
            for (int i = 1; i <= 4; i++)
            {
                float y = padTop + chartHeight - chartHeight * i / 4f;
                canvas.DrawLine(0, y, width, y);
                canvas.FontSize = 9;
                canvas.FontColor = Color.FromArgb("#AAAAAA");
                canvas.DrawString((maxVal * i / 4).ToString(), 2, y - 8, 30, 16,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }

            for (int i = 0; i < values.Length; i++)
            {
                float barHeight = values[i] == 0 ? 2 : (float)values[i] / maxVal * chartHeight;
                float x = gap + i * totalBar;
                float y = padTop + chartHeight - barHeight;

                canvas.FillColor = BarColors[i];
                canvas.FillRoundedRectangle(x, y, barWidth, barHeight, 6);

                canvas.FontSize = 11;
                canvas.FontColor = BarColors[i];
                canvas.DrawString(values[i].ToString(), x, y - 16, barWidth, 16,
                    HorizontalAlignment.Center, VerticalAlignment.Center);

                canvas.FontSize = 10;
                canvas.FontColor = Color.FromArgb("#777777");
                canvas.DrawString(Labels[i], x - 5, height - padBottom + 4, barWidth + 10, padBottom,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }
    }
}