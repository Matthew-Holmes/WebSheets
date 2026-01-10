namespace Agents
{
    // TODO - build --> parametrise --> employ, pattern
    public abstract class ParametrisedAgentBase : AgentBase
    {
        public List<Parameter<double>>    ContinuousParameters { get; } = new List<Parameter<double>>();
        public List<Parameter<int>>       DiscreteParameters   { get; } = new List<Parameter<int>>();
        public Dictionary<String, String> StringParameters     { get; } = new Dictionary<String, String>();

        public ParametrisedAgentBase(SemaphoreSlim? globalSemaphore = null) 
            : base(globalSemaphore) { }

        public Parameter<double> ContinuousParameter(String name)
        {
            return ContinuousParameters.Where(p => p.Name == name).First();            
        }

        public Parameter<int> DiscreteParameter(String name)
        {
            return DiscreteParameters.Where(p => p.Name == name).First();
        }
    }
}
