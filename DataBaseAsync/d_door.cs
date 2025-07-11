
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// d_door
    /// </summary>
    [Table("d_door")]
    public class d_door
    {

        /// <summary>
        /// Id
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 门岗编号
        /// </summary>
        public String doorbh { get; set; }

        /// <summary>
        /// 门岗名称
        /// </summary>
        public String doorname { get; set; }

        /// <summary>
        /// 门岗类型
        /// </summary>
        public String type { get; set; }
        /// <summary>
        /// 门岗数据库连接
        /// </summary>
        public String dbconstr { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public String Remark { get; set; }

        /// <summary>
        /// 使用状态
        /// </summary>
        public String StatusFlag { get; set; }

        /// <summary>
        /// 创建日期
        /// </summary>
        public DateTime? CreateDate { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public String CreateOperator { get; set; }

        /// <summary>
        /// 修改日期
        /// </summary>
        public DateTime? ModifyDate { get; set; }

        /// <summary>
        /// 修改人
        /// </summary>
        public String ModifyOperator { get; set; }
        /// <summary>
        /// 门岗电脑IP
        /// </summary>
        public String pcip { get; set; }
    }
}