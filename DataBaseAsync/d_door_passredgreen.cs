using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// d_door_passredgreen
    /// </summary>
    [Table("d_door_passredgreen")]
    public class d_door_passredgreen
    {

        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 门岗编号
        /// </summary>
        public String doorbh { get; set; }

        /// <summary>
        /// 道路编号
        /// </summary>
        public String roadnum { get; set; }

        /// <summary>
        /// 道路名称
        /// </summary>
        public String roadname { get; set; }

        /// <summary>
        /// 通道编号
        /// </summary>
        public String passnum { get; set; }

        /// <summary>
        /// 通道名称
        /// </summary>
        public String passname { get; set; }

        /// <summary>
        /// 读卡器IP地址
        /// </summary>
        public String ip { get; set; }

        /// <summary>
        /// 通道类型(0 进 1出)
        /// </summary>
        public String type { get; set; }

        /// <summary>
        /// 通行状态(0 不可通行 1可通行)
        /// </summary>
        public String usingflag { get; set; }

        /// <summary>
        /// 进红绿灯Ip
        /// </summary>
        public String redgreen { get; set; }

        /// <summary>
        /// 端口
        /// </summary>
        public String redgreenport { get; set; }

        /// <summary>
        /// 模式
        /// </summary>
        public Int32? mode { get; set; }

        /// <summary>
        /// 进红灯端口
        /// </summary>
        public Int32? redindex { get; set; }

        /// <summary>
        /// 进绿灯端口
        /// </summary>
        public Int32? greenindex { get; set; }

        /// <summary>
        /// 心跳时间
        /// </summary>
        public DateTime? Lasttime { get; set; }

        /// <summary>
        /// 进LedIP地址
        /// </summary>
        public String led_ip { get; set; }

        /// <summary>
        /// 网控IP地址
        /// </summary>
        public String controler_id { get; set; }

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

    }
}