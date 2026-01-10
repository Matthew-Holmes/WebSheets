namespace Agents
{
    public class Parameter<T> where T : IComparable<T>
    {
        // class for holding parameters restricted to ranges
        public String Name { get; }

        public T UpperBound { get; }
        public T LowerBound { get; }

        // the soft bounds are there to provide basic distributional information on the parameter
        // and to provided sensible bounds if full range parameters are presented to the user

        private T _softUpperBound;
        public T SoftUpperBound
        {
            get => _softUpperBound;
            set
            {
                if (Comparer<T>.Default.Compare(value, UpperBound) <= 0 && Comparer<T>.Default.Compare(value, LowerBound) >= 0)
                {
                    _softUpperBound = value;
                }
            }
        }
        private T _softLowerBound;

        public T SoftLowerBound
        {
            get => _softLowerBound;
            set
            {
                if (Comparer<T>.Default.Compare(value, UpperBound) <= 0 && Comparer<T>.Default.Compare(value, LowerBound) >= 0)
                {
                    _softLowerBound = value;
                }
            }
        }

        private T _val;
        public T Value
        {
            get => _val;
            set
            {
                if (Comparer<T>.Default.Compare(value, UpperBound) <= 0 && Comparer<T>.Default.Compare(value, LowerBound) >= 0)
                {
                    _val = value;
                }
            }
        }

        public Parameter(String name, T val, T ub, T lb)
        {
            Name = name;

            if (Comparer<T>.Default.Compare(val, ub) > 0 || Comparer<T>.Default.Compare(val, lb) < 0)
            {
                throw new Exception("Value provided exceeds bounds");
            }

            UpperBound = ub;
            LowerBound = lb;

            Value = val;

            SoftUpperBound = ub;
            SoftLowerBound = lb;
        }

        public Parameter(String name, T Value, T UpperBound, T LowerBound, T SoftUpperBound, T SoftLowerBound) 
            : this(name, Value, UpperBound, LowerBound)
        {
            if (Comparer<T>.Default.Compare(SoftUpperBound, UpperBound) > 0 || Comparer<T>.Default.Compare(SoftUpperBound, LowerBound) < 0)
            {
                throw new Exception("Soft upper bound provided exceeds bounds");
            }

            if (Comparer<T>.Default.Compare(SoftLowerBound, UpperBound) > 0 || Comparer<T>.Default.Compare(SoftLowerBound, LowerBound) < 0)
            {
                throw new Exception("Soft lower bound provided exceeds bounds");
            }

            this.SoftUpperBound = SoftUpperBound;
            this.SoftLowerBound = SoftLowerBound;
        }
    }
}
