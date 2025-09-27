using Microsoft.EntityFrameworkCore.Storage.Internal;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Coldairarrow.bgmj.Entity
{
    /// <summary>
    /// 人员信息管理
    /// </summary>
    [Table("d_man")]
    public class d_man
    {
        /// <summary>
        /// 主键
        /// </summary>
        [Key, Column(Order = 1)]
        public String Id { get; set; }

        /// <summary>
        /// 虚拟卡ID
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
        /// 服务单位id
        /// </summary>
        public String qwdwid { get; set; }

        /// <summary>
        /// 所属单位id
        /// </summary>
        public String dwdmid { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public String Note { get; set; }

        /// <summary>
        /// 照片是否上传
        /// </summary>
        public String photourl { get; set; }

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
        /// 经办人
        /// </summary>
        public String transactor { get; set; }
        /// <summary>
        /// 最后下发设备时间
        /// </summary>
        public DateTime? tolastdevdtime { get; set; }
        /// <summary>
        /// 最后通行时间
        /// </summary>
        public DateTime? using_dtime { get; set; }

        /// <summary>
        /// 标签
        /// </summary>
        public string Tag { get; set; }
        /// <summary>
        /// 黑名单原因
        /// </summary>
        [NotMapped]
        public String doors { get; set; }
        /// <summary>
        /// 黑名单原因
        /// </summary>
        [NotMapped]
        public String blackreason { get; set; }
        /// <summary>
        /// 黑名单原因
        /// </summary>
        [NotMapped]
        public DateTime? blacktime { get; set; }
        /// <summary>
        /// 人员代码
        /// </summary>
        public String person_code { get; set; }
        /// <summary>
        /// 民族
        /// </summary>
        public String nation { get; set; }
        /// <summary>
        /// 申请Id
        /// </summary>
        public String apply_id { get; set; }


        /// <summary>
        /// 出生日期
        /// </summary>
        public DateTime? brithday { get; set; }

        /// <summary>
        /// 身份证签发机关
        /// </summary>
        public String sfz_sign_unit { get; set; }

        /// <summary>
        /// 身份证发证日期
        /// </summary>
        public DateTime? startdate { get; set; }

        /// <summary>
        /// 身份证失效日期
        /// </summary>
        public String enddate { get; set; }


        /// <summary>
        /// personstate
        /// </summary>
        public String personstate { get; set; }
        /// <summary>
        /// zt
        /// </summary>
        public String zt { get; set; }

        /// <summary>
        /// 身份证正本url
        /// </summary>
        public String idcardFront { get; set; }

        /// <summary>
        /// 身份证副本url
        /// </summary>
        public String idcardBront { get; set; }

        /// <summary>
        /// 身份证副本url
        /// </summary>
        public String isDriver { get; set; }

        /// <summary>
        /// 是否为新注册人员
        /// </summary>
        public String newManFlag { get; set; }

        /// <summary>
        /// 新联人员状态
        /// </summary>
        public String xluserstate { get; set; }

        /// <summary>
        /// 申请人
        /// </summary>
        public String SQR { get; set; }
        /// <summary>
        /// 申请时间
        /// </summary>
        public DateTime? SQSJ { get; set; }
        /// <summary>
        /// 审核人
        /// </summary>
        public String SHR { get; set; }
        /// <summary>
        /// 审核时间
        /// </summary>
        public DateTime? SHSJ { get; set; }
		/// <summary>
		/// 认证时间
		/// </summary>
		public DateTime? certifyDate { get; set; }
		/// <summary>
		/// 是否超60年龄
		/// </summary>
		public String overageCheck { get; set; } = "0";
		/// <summary>
		/// 工作区域
		/// </summary>
		public String throughArea { get; set; }
        public string facemark { get; set; }
        //标识注册人脸
        public string faceflag { get; set; }
    }
}