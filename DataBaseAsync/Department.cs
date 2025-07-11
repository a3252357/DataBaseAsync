using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataBaseAsync
{
    public class Base_Department
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string FullName { get; set; }

        public string DePartName { get; set; }

        public string ErpId { get; set; }

        public string ParentId { get; set; }

        public string nflag { get; set; }
    }
}
