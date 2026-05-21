using System;
using System.Collections.Generic;

namespace UpdatePlanner
{
    public enum RepeatMode { None, Weekly, Monthly }

    public class ScheduleConfig
    {
        public DateTime      StartDateTime { get; set; }
        public DateTime?     EndDateTime   { get; set; }
        public RepeatMode    Repeat        { get; set; } = RepeatMode.None;
        public List<DayOfWeek> WeekDays   { get; set; } = new List<DayOfWeek>();
        public List<int>     MonthDays    { get; set; } = new List<int>();

        /// <summary>after 이후의 다음 실행 시각을 반환합니다. 더 이상 없으면 null.</summary>
        public DateTime? GetNextFireTime(DateTime after)
        {
            if (Repeat == RepeatMode.None)
            {
                if (StartDateTime > after &&
                    (!EndDateTime.HasValue || StartDateTime <= EndDateTime.Value))
                    return StartDateTime;
                return null;
            }

            TimeSpan tod = StartDateTime.TimeOfDay;
            DateTime candidate = after.Date.Add(tod);
            if (candidate <= after) candidate = candidate.AddDays(1);

            int maxDays = Repeat == RepeatMode.Weekly ? 14 : 32;
            for (int i = 0; i < maxDays; i++, candidate = candidate.AddDays(1))
            {
                if (candidate < StartDateTime.Date) continue;
                if (EndDateTime.HasValue && candidate.Date > EndDateTime.Value.Date) return null;

                if (Repeat == RepeatMode.Weekly  && !WeekDays.Contains(candidate.DayOfWeek)) continue;
                if (Repeat == RepeatMode.Monthly && !MonthDays.Contains(candidate.Day))      continue;

                return candidate;
            }
            return null;
        }

        public string GetSummary()
        {
            string time = StartDateTime.ToString("HH:mm");
            string end  = EndDateTime.HasValue ? $" ~ {EndDateTime.Value:yyyy-MM-dd}" : "";

            switch (Repeat)
            {
                case RepeatMode.None:
                    return $"{StartDateTime:yyyy-MM-dd} {time}  (1회)";
                case RepeatMode.Weekly:
                    string wdays = WeekDays.Count > 0
                        ? string.Join(" ", WeekDays.ConvertAll(DayKor))
                        : "(요일 미선택)";
                    return $"{StartDateTime:yyyy-MM-dd}{end}  |  매주 {wdays}  {time}";
                case RepeatMode.Monthly:
                    string mdays = MonthDays.Count > 0
                        ? string.Join(", ", MonthDays) + "일"
                        : "(일자 미선택)";
                    return $"{StartDateTime:yyyy-MM-dd}{end}  |  매달 {mdays}  {time}";
                default:
                    return "";
            }
        }

        private static string DayKor(DayOfWeek d)
        {
            string[] k = { "일", "월", "화", "수", "목", "금", "토" };
            return k[(int)d];
        }
    }
}
