namespace InternetConnectionScheduler
{
    internal class Rule
    {
        private object _lock { get; set; } = new object();

        private TimeOnly start;

        public TimeOnly Start
        {
            get { return start; }
            set
            {
                lock (_lock)
                {
                    start = value;
                }
            }
        }

        private TimeOnly end;

        public TimeOnly End
        {
            get { return end; }
            set
            {
                lock (_lock)
                {
                    end = value;
                }
            }
        }
    }
}
