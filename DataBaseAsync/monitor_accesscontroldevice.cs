using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.CHCSDK.Entity
{
    /// <summary>
    /// 门禁主机设备表
    /// </summary>
    [Table("monitor_accesscontroldevice")]
    public class monitor_accesscontroldevice
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 设备类型
        /// </summary>
        public String DeviceType { get; set; }

        /// <summary>
        /// 主键名称
        /// </summary>
        public String DeviceName { get; set; }

        /// <summary>
        /// 设备Ip
        /// </summary>
        public String DeviceIp { get; set; }

        /// <summary>
        /// 设备端口
        /// </summary>
        public String DevicePort { get; set; }

        /// <summary>
        /// 设备用户名
        /// </summary>
        public String DeviceUserName { get; set; }

        /// <summary>
        /// 设备密码
        /// </summary>
        public String DevicePassWord { get; set; }

        /// <summary>
        /// 上次读取日志时间
        /// </summary>
        public DateTime? LastLogDate { get; set; }

        /// <summary>
        /// 上次读取日志时间
        /// </summary>
        public DateTime? AsyncDate { get; set; }

        /// <summary>
        /// LED设备提示音
        /// </summary>
        public String LedMsg { get; set; }

        /// <summary>
        /// 字符编码
        /// </summary>
        public String StringCode { get; set; }

        /// <summary>
        /// 字符编码
        /// </summary>
        public String mantype { get; set; } = "0";
        //0表示所有都下发  1表示只下发正式工 2表示只下发服务人员

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