using System.Collections;

namespace SecurityActivityQueuePrototype;

internal class SecurityActivityLoader : IEnumerable<SecurityActivity>
{
    private readonly bool _gapLoader;

    private readonly int _from;
    private readonly int _to;
    private readonly IEnumerable<int> _gaps;
    private readonly bool _executingUnprocessedActivities;
    private readonly DataHandler _dataHandler;
    private readonly int _pageSize = 200;


    public SecurityActivityLoader(int from, int to, bool executingUnprocessedActivities, DataHandler dataHandler)
    {
        _gapLoader = false;
        _from = from;
        _to = to;
        _executingUnprocessedActivities = executingUnprocessedActivities;
        _dataHandler = dataHandler;
    }
    public SecurityActivityLoader(IEnumerable<int> gaps, bool executingUnprocessedActivities, DataHandler dataHandler)
    {
        _gapLoader = true;
        _gaps = gaps;
        _executingUnprocessedActivities = executingUnprocessedActivities;
        _dataHandler = dataHandler;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
    public IEnumerator<SecurityActivity> GetEnumerator()
    {
        if (_gapLoader)
        {
            foreach (var gap in _gaps)
            {
                yield return new SecurityActivity(gap, 10)
                {
                    FromDatabase = true,
                    IsUnprocessedActivity = _executingUnprocessedActivities
                };
            }
        }
        else
        {
            for (int id = _from; id <= _to; id++)
            {
                yield return new SecurityActivity(id, 10)
                {
                    FromDatabase = true,
                    IsUnprocessedActivity = _executingUnprocessedActivities
                };
            }
        }
    }

}