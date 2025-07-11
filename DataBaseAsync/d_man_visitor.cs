using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 访客人员信息管理
    /// </summary>
    [Table("d_man_visitor")]
    public class d_man_visitor
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 虚拟卡Id
        /// </summary>
        public String cardId { get; set; }

        /// <summary>
        /// 虚拟卡号
        /// </summary>
        public String cardNo { get; set; }

        /// <summary>
        /// 姓名
        /// </summary>
        public String name { get; set; }

        /// <summary>
        /// 性别
        /// </summary>
        public String sex { get; set; }

        /// <summary>
        /// 身份证号
        /// </summary>
        public String sfzh { get; set; }

        /// <summary>
        /// 开始日期
        /// </summary>
        public DateTime? date_begin { get; set; }

        /// <summary>
        /// 结束日期
        /// </summary>
        public DateTime? date_end { get; set; }

        /// <summary>
        /// 人员卡类型
        /// </summary>
        public String zl { get; set; }

        /// <summary>
        /// 人员卡状态
        /// </summary>
        public String Status { get; set; }

        /// <summary>
        /// 服务单位
        /// </summary>
        public String qwdw { get; set; }

        /// <summary>
        /// 所属单位
        /// </summary>
        public String dwdm { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public String Note { get; set; }

        /// <summary>
        /// 照片是否上传
        /// </summary>
        public String photourl { get; set; }

        /// <summary>
        /// 附件
        /// </summary>
        public String reason { get; set; }

        /// <summary>
        /// 附件
        /// </summary>
        public String reasonurl { get; set; }

        /// <summary>
        /// 联系方式
        /// </summary>
        public String telephone { get; set; }

        /// <summary>
        /// 地址
        /// </summary>
        public String address { get; set; }

        /// <summary>
        /// 信息来源
        /// </summary>
        public String sources { get; set; }

        /// <summary>
        /// 进出次数是否限制
        /// </summary>
        public String limit_num_flag { get; set; }

        /// <summary>
        /// 允许最大进出次数
        /// </summary>
        public Int32? most_in_out_num { get; set; }

        /// <summary>
        /// 在厂状态
        /// </summary>
        public String using_state { get; set; }

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
        /// ERP工号
        /// </summary>
        public String userid { get; set; }

        /// <summary>
        /// ERPid
        /// </summary>
        public String erpid { get; set; }

        /// <summary>
        /// 安全级别
        /// </summary>
        public String level { get; set; }

        /// <summary>
        /// 人员状态
        /// </summary>
        public String gsbz { get; set; }

        /// <summary>
        /// 工号
        /// </summary>
        public Int32 EmployeeNo { get; set; }

        /// <summary>
        /// 中间表Id
        /// </summary>
        public String tempid { get; set; }

        /// <summary>
        /// 办卡人
        /// </summary>
        public String transactor { get; set; }

        /// <summary>
        /// 版本号
        /// </summary>
        public String version { get; set; }

        /// <summary>
        /// 门岗
        /// </summary>
        public String door { get; set; }
        /// <summary>
        /// 最后下发设备时间
        /// </summary>
        public DateTime? tolastdevdtime { get; set; }
        /// <summary>
        /// 最后通行时间
        /// </summary>
        public DateTime? using_dtime { get; set; }

        /// <summary>
        /// 民族
        /// </summary>
        public String SQR { get; set; }
        /// <summary>
        /// 进出厂申请id
        /// </summary>
        public DateTime? SQSJ { get; set; }
        /// <summary>
        /// 民族
        /// </summary>
        public String SHR { get; set; }
        /// <summary>
        /// 进出厂申请id
        /// </summary>
        public DateTime? SHSJ { get; set; }

        /// <summary>
        /// 民族
        /// </summary>
        public String apply_id { get; set; }
        /// <summary>
        /// 访客
        /// </summary>
        public String type { get; set; }
        /// <summary>
        /// 区域
        /// </summary>
        public String throughArea { get; set; }
    }
}