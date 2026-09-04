using System;
using System.Collections.Generic;

namespace slider.Models
{
    public class PlaylistPeriod
    {
        public string Name { get; set; } = "";

        public DateTime StartDateTime { get; set; } = DateTime.Today;

        public DateTime EndDateTime { get; set; } = DateTime.Today.AddDays(1);

        // Если false — период работает непрерывно от StartDateTime до EndDateTime.
        // Если true — дополнительно учитываются выбранные дни недели.
        public bool UseWeekDays { get; set; } = false;

        // Дни недели, в которые разрешён показ.
        public List<DayOfWeek> ActiveDays { get; set; } = new();

        public List<SlideItem> Slides { get; set; } = new();

        public bool IsActiveAt(DateTime dateTime)
        {
            // Сначала проверяем абсолютные границы периода.
            if (dateTime < StartDateTime || dateTime >= EndDateTime)
                return false;

            // Старый режим: никаких ограничений по дням недели.
            if (!UseWeekDays)
                return true;

            // Защита от некорректного плейлиста.
            if (ActiveDays == null || ActiveDays.Count == 0)
                return false;

            // В режиме дней недели достаточно,
            // чтобы сегодняшний день был разрешён.
            return ActiveDays.Contains(dateTime.DayOfWeek);
        }
    }
}