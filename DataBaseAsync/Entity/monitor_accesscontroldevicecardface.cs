using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.CHCSDK.Entity
{
    /// <summary>
    /// 门禁主机设备卡数据
    /// </summary>
    [Table("monitor_accesscontroldevicecardface")]
    public class monitor_accesscontroldevicecardface
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 卡Id
        /// </summary>
        public String CardId { get; set; }

        /// <summary>
        /// 版本
        /// </summary>
        public Int32 version { get; set; } = 0;

        /// <summary>
        /// 图片路径
        /// </summary>
        public String Filepath { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public String Status { get; set; }

    }
}