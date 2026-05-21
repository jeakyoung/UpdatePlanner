using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace UpdatePlanner
{
    public partial class ScheduleDetailDialog : Window
    {
        public ScheduleConfig Result { get; private set; }

        public ScheduleDetailDialog(ScheduleConfig existing = null)
        {
            InitializeComponent();

            // 매달 일자 체크박스 생성 (1 ~ 31)
            for (int d = 1; d <= 31; d++)
            {
                var cb = new CheckBox
                {
                    Content = d.ToString(),
                    Tag     = d,
                    Width   = 42,
                    Margin  = new Thickness(0, 0, 4, 4)
                };
                WrapMonthDays.Children.Add(cb);
            }

            // 기존 설정 불러오기
            if (existing != null)
            {
                StartDate.SelectedDate = existing.StartDateTime.Date;
                TxtStartHour.Text      = existing.StartDateTime.Hour.ToString("D2");
                TxtStartMinute.Text    = existing.StartDateTime.Minute.ToString("D2");

                if (existing.EndDateTime.HasValue)
                {
                    RbEndDate.IsChecked   = true;
                    EndDate.SelectedDate  = existing.EndDateTime.Value.Date;
                    EndDate.IsEnabled     = true;
                }

                switch (existing.Repeat)
                {
                    case RepeatMode.Weekly:
                        RbWeekly.IsChecked = true;
                        foreach (var dow in existing.WeekDays)
                            SetWeekDayCheck(dow, true);
                        break;
                    case RepeatMode.Monthly:
                        RbMonthly.IsChecked = true;
                        foreach (int day in existing.MonthDays)
                            SetMonthDayCheck(day, true);
                        break;
                    default:
                        RbNone.IsChecked = true;
                        break;
                }
            }
            else
            {
                StartDate.SelectedDate = DateTime.Today;
                TxtStartHour.Text      = "00";
                TxtStartMinute.Text    = "00";
            }
        }

        // ─── 종료 모드 ──────────────────────────────────────────────────────

        private void RbEndMode_Checked(object sender, RoutedEventArgs e)
        {
            if (EndDate != null)
                EndDate.IsEnabled = RbEndDate?.IsChecked == true;
        }

        // ─── 반복 모드 패널 전환 ─────────────────────────────────────────────

        private void RbRepeat_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelNone == null) return;

            PanelNone.Visibility    = RbNone?.IsChecked    == true ? Visibility.Visible : Visibility.Collapsed;
            PanelWeekly.Visibility  = RbWeekly?.IsChecked  == true ? Visibility.Visible : Visibility.Collapsed;
            PanelMonthly.Visibility = RbMonthly?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─── 시간 입력 ───────────────────────────────────────────────────────

        private void TimeBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^\d$");
        }

        private void TxtStartHour_LostFocus(object sender, RoutedEventArgs e)
        {
            int h = int.TryParse(TxtStartHour.Text, out int v) ? Math.Max(0, Math.Min(23, v)) : 0;
            TxtStartHour.Text = h.ToString("D2");
        }

        private void TxtStartMinute_LostFocus(object sender, RoutedEventArgs e)
        {
            int m = int.TryParse(TxtStartMinute.Text, out int v) ? Math.Max(0, Math.Min(59, v)) : 0;
            TxtStartMinute.Text = m.ToString("D2");
        }

        // ─── 확인 ────────────────────────────────────────────────────────────

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!StartDate.SelectedDate.HasValue)
            {
                MessageBox.Show("시작 날짜를 선택하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int hour = int.TryParse(TxtStartHour.Text,   out int h) ? Math.Max(0, Math.Min(23, h)) : 0;
            int min  = int.TryParse(TxtStartMinute.Text, out int m) ? Math.Max(0, Math.Min(59, m)) : 0;

            var cfg = new ScheduleConfig
            {
                StartDateTime = StartDate.SelectedDate.Value.Date.AddHours(hour).AddMinutes(min),
                Repeat        = RbWeekly?.IsChecked  == true ? RepeatMode.Weekly
                              : RbMonthly?.IsChecked == true ? RepeatMode.Monthly
                              : RepeatMode.None
            };

            if (RbEndDate?.IsChecked == true && EndDate.SelectedDate.HasValue)
                cfg.EndDateTime = EndDate.SelectedDate.Value.Date.AddHours(23).AddMinutes(59);

            if (cfg.Repeat == RepeatMode.Weekly)
            {
                if (ChkMon.IsChecked == true) cfg.WeekDays.Add(DayOfWeek.Monday);
                if (ChkTue.IsChecked == true) cfg.WeekDays.Add(DayOfWeek.Tuesday);
                if (ChkWed.IsChecked == true) cfg.WeekDays.Add(DayOfWeek.Wednesday);
                if (ChkThu.IsChecked == true) cfg.WeekDays.Add(DayOfWeek.Thursday);
                if (ChkFri.IsChecked == true) cfg.WeekDays.Add(DayOfWeek.Friday);
                if (ChkSat.IsChecked == true) cfg.WeekDays.Add(DayOfWeek.Saturday);
                if (ChkSun.IsChecked == true) cfg.WeekDays.Add(DayOfWeek.Sunday);

                if (cfg.WeekDays.Count == 0)
                {
                    MessageBox.Show("매주 반복 시 요일을 하나 이상 선택하세요.", "입력 오류",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (cfg.Repeat == RepeatMode.Monthly)
            {
                foreach (CheckBox cb in WrapMonthDays.Children)
                    if (cb.IsChecked == true && cb.Tag is int day)
                        cfg.MonthDays.Add(day);

                if (cfg.MonthDays.Count == 0)
                {
                    MessageBox.Show("매달 반복 시 일자를 하나 이상 선택하세요.", "입력 오류",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // 1회 실행이면 시작 일시가 미래여야 함
            if (cfg.Repeat == RepeatMode.None && cfg.StartDateTime <= DateTime.Now)
            {
                MessageBox.Show("예약 시간이 현재 시간보다 이후여야 합니다.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = cfg;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        // ─── 헬퍼 ────────────────────────────────────────────────────────────

        private void SetWeekDayCheck(DayOfWeek dow, bool check)
        {
            switch (dow)
            {
                case DayOfWeek.Monday:    ChkMon.IsChecked = check; break;
                case DayOfWeek.Tuesday:   ChkTue.IsChecked = check; break;
                case DayOfWeek.Wednesday: ChkWed.IsChecked = check; break;
                case DayOfWeek.Thursday:  ChkThu.IsChecked = check; break;
                case DayOfWeek.Friday:    ChkFri.IsChecked = check; break;
                case DayOfWeek.Saturday:  ChkSat.IsChecked = check; break;
                case DayOfWeek.Sunday:    ChkSun.IsChecked = check; break;
            }
        }

        private void SetMonthDayCheck(int day, bool check)
        {
            foreach (CheckBox cb in WrapMonthDays.Children)
                if (cb.Tag is int d && d == day) { cb.IsChecked = check; break; }
        }
    }
}
