using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.Exceptions
{
    public class BusinessException : Exception
    {
        public BusinessException(string key) : base(key) { }
    }
}
