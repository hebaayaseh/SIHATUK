namespace Sehatak.Application.Common
{
    public class PagedRequest
    {
        private int _pageSize = 10;

        public int PageNumber { get; set; } = 1;

        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value <= 0 ? 10 : (value > 50 ? 50 : value);
        }
    }
}