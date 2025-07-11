using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    public class cbg_door_devrelay_base
    {

        /// <summary>
        /// Id
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 设备Id
        /// </summary>
        public String mid { get; set; }

        /// <summary>
        /// 继电器Ip 
        /// </summary>
        public String relayip { get; set; }

        /// <summary>
        /// 继电器端口
        /// </summary>
        public String relayport { get; set; }

        /// <summary>
        /// 继电器接线端号
        /// </summary>
        public String linenum { get; set; }

        /// <summary>
        /// 摄像头IP
        /// </summary>
        public String cameraip { get; set; }

        /// <summary>
        /// 摄像头端口
        /// </summary>
        public String cameraport { get; set; }

        /// <summary>
        /// 摄像头用户名
        /// </summary>
        public String camerauser { get; set; }

        /// <summary>
        /// 摄像头密码
        /// </summary>
        public String camerapwd { get; set; }

        /// <summary>
        /// 门岗
        /// </summary>
        public String door { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public String note { get; set; }

        /// <summary>
        /// 删除标识
        /// </summary>
        public String IsDelete { get; set; }

        /// <summary>
        /// 描述
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
        /// 是否主要的
        /// </summary>
        public bool IsMain { get; set; }


        /// <summary>
        /// 等待毫秒
        /// </summary>
        public int Delay { get; set; }
    }
}