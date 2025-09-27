using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataBaseAsync
{
    [Table("base_config")]
    public class base_config
    {
        [Key]
        [Column(Order = 1)]
        public string Id { get; set; }

        public string code { get; set; }

        public string value { get; set; }

        public string name { get; set; }

        public string type { get; set; }

        public string Remark { get; set; }

        public string StatusFlag { get; set; }

        public DateTime? CreateDate { get; set; }

        public string CreateOperator { get; set; }

        public DateTime? ModifyDate { get; set; }

        public string ModifyOperator { get; set; }
    }
}
