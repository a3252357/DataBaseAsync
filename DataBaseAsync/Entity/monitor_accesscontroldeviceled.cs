using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.CHCSDK.Entity
{
    /// <summary>
    /// 门禁主机LED对应表
    /// </summary>
    [Table("monitor_accesscontroldeviceled")]
    public class monitor_accesscontroldeviceled
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 对应设备ID
        /// </summary>
        public String DeviceId { get; set; }

        /// <summary>
        /// LEDIp
        /// </summary>
        public String LEDIp { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public String ReMark { get; set; }

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

    }
}