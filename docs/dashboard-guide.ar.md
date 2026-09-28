# دليل قراءة HUMAIN Coordination Dashboard

مرجع مطابق لتعريفات الـ Measures وتقارير Dark وLight المرفقة، بتاريخ 28 سبتمبر 2026. الأسماء التقنية بين الأقواس تسمح بمراجعة المعادلات في `powerbi/HUMAIN.SemanticModel/model.bim`.

## قبل قراءة أي رقم

- **رصيد Stock:** الحالة عند آخر Snapshot متاح قبل نهاية الفترة؛ ليس مجموع الأيام.
- **حركة Flow:** الانتقالات المرصودة خلال الفترة المختارة، مع استبعاد أول Snapshot لأنه خط أساس.
- تاريخ الحركة هو تاريخ اكتشافها في التصدير، وليس بالضرورة وقت تنفيذ الحل في النموذج.
- Severity والتخصصات مشتقة من تسمية Clash Test؛ التصنيف لا يقيس خطورة هندسية مستقلة.
- Reviewed يظل Actionable. Approved مقبول ومستبعد من المطلوب حله، لكنه ليس حلًا هندسيًا. Resolved مستبعد فورًا حتى قبل Compact.
- اختفاء GUID غير المحلول يُحسب حلًا حسب قاعدة المشروع. لذلك يجب تصدير نفس النطاق بالكامل؛ التصدير الناقص قد يبدو كأنه إنجاز.
- Compact بعد تسجيل Resolved لا يحسب حلًا ثانيًا. عودة نفس GUID للعمل تسجل Returned to Action. GUID جديد يمثل هوية جديدة.
- الكروت تستجيب للفلاتر ذات العلاقة وتفاعلات الرسوم. لا تقارن رقمين قبل مطابقة الفترة والنطاق والتخصص والاختبار.

## Executive Overview

المصدر الأساسي لهذه الكروت `OperationalProgress.csv`، وليس قيمة OpenClashes القديمة في `DashboardKPI.csv`.

| الكارد / Measure | يمثل ماذا وكيف يحسب؟ | كيف تتكلم عنه مع التيم؟ |
|---|---|---|
| ACTIONABLE OPEN / Open Clashes | رصيد Actionable في نهاية الفترة؛ يستبعد Approved وResolved ويشمل Reviewed والحالات غير المعروفة. | حجم الشغل الذي ما زال يحتاج إجراء تنسيق. |
| NEW ACTIONABLE / New | مجموع NewActionable خلال الفترة، دون خط الأساس؛ لا يشمل أول ظهور بحالة Approved أو Resolved. | حجم العمل الجديد الداخل إلى القائمة، وليس إجمالي كل GUID جديد بأي حالة. |
| RESOLVED · ALL / Resolved | مجموع Resolved خلال الفترة: بالـ Status أو الاختفاء. يشمل ما كان Approved قبل الحل. | حالات الحل المرصودة، وقد لا يساوي مقدار تقليل العمل المفتوح. |
| NET REDUCTION · INCL. APPROVALS / Net Actionable Reduction | ResolvedFromActionable + ApprovedFromActionable − NewActionable − ReturnedActionable. | الموجب تحسن صافٍ، السالب زيادة صافية، والصفر قد يخفي حركة كبيرة متعادلة. القبول يساهم في الانخفاض. |
| RESOLUTION RATE / Resolution Rate | ActionableResolved ÷ (ActionablePrevious + NewActionable + Returned to Action). تظهر فارغة إذا المقام صفر. | نسبة ما حُلّ من العمل المتاح للفترة؛ Approved لا يدخل البسط، وليست Resolved All ÷ Open الحالي. |
| FORECAST · INCLUDES APPROVALS / Forecast Display | نهاية تقديرية = تاريخ آخر Snapshot + تقريب لأعلى لـ Actionable ÷ متوسط صافي الانخفاض اليومي. المتوسط يستخدم الفواصل المرصودة المنتهية في آخر 7 أيام ويقسم على مجموع مدتها الفعلية. | توقع نفاد قائمة العمل بنفس المعدل، ويشمل أثر Approved؛ ليس موعد تسليم معتمدًا. |
| TOTAL CURRENT / Total Current | كل السجلات الموجودة فعليًا في Snapshot النهاية، بكل الحالات. | حجم سجل التصدير: Actionable + Approved + Resolved Retained. |
| APPROVED · ACCEPTED / Approved Current | رصيد السجلات الموجودة حاليًا بحالة Approved. | تعارضات مقبولة تحتاج أن يكون قبولها مبررًا؛ ليست حلولًا هندسية. |
| NEW APPROVALS / New Approvals | انتقالات مرصودة إلى Approved خلال الفترة؛ أول ظهور Approved ليس حركة قبول. | حجم تغييرات القبول في الفترة، وليس رصيد Approved الحالي. |
| NET REDUCTION · EXCL. APPROVALS / Net Reduction excl Approvals | صافي الانخفاض الكلي ناقص القبول من Actionable؛ يساوي الحل من المفتوح ناقص الجديد والعائد. | يفصل أثر القبول، لكنه لا يثبت حلًا هندسيًا؛ الحل ما زال يشمل اختفاء GUID. |
| ACTIONABLE · LAST 7 DAYS / Actionable Change 7d | الرصيد عند Selected Snapshot ناقص الرصيد عند أحدث لقطة تسبقها بسبعة أيام أو أكثر. عند تاريخ أقصر تستخدم أول لقطة سابقة مع بيان المدة الحقيقية. | الموجب زيادة في العمل (أحمر)، والسالب انخفاض (أخضر). ليس هو مجموع الحركات في سلايسر Last 7 days. |

### رسائل Forecast

| الرسالة | معناها |
|---|---|
| No snapshot | لا توجد لقطة قبل نهاية الفترة. |
| Review data | توجد تنبيهات جودة تحتاج إجراء في نافذة التوقع الأخيرة؛ التوقع محجوب حتى مراجعة البيانات. |
| No work remaining | رصيد Actionable صفر، بعد اجتياز فحص الجودة. |
| Need 3 days | أقل من ثلاثة تواريخ Snapshot مختلفة في النافذة الأخيرة؛ ليست ثلاث لقطات في يوم واحد. |
| No net burn | لا يوجد معدل انخفاض صافٍ موجب يسمح بتوقع النهاية. |
| تاريخ | تقدير مشروط باستمرار المعدل الحالي وثبات طريقة التصدير والنطاق. |

فحص جودة التوقع يغطي آخر 7 أيام بالنسبة للقطة المختارة ويتجاهل فلتر الاختبار. لذلك قد ترى Review data رغم أن فترة Today نفسها لا تحتوي تنبيهًا.

## Clash Test Performance

| الكارد / Measure | التعريف والمصدر | استخدامه |
|---|---|---|
| TESTS IN SCOPE / Tests in Scope | عدد TestName المميز في TestPerformance عند Snapshot النهاية بعد الفلاتر. | حجم نطاق الاختبارات المُمثّل؛ لا يفترض رقمًا ثابتًا دائمًا. |
| IMPROVING TESTS / Tests Improving | عدد الاختبارات المختارة ذات Net Reduction موجب في الفترة. | كم اختبارًا تحسن صافيًا، وليس كم اختبار شهد أي حل. |
| INCREASING TESTS / Tests Increasing | عدد الاختبارات ذات Net Reduction سالب. | الاختبارات التي يتراكم فيها عمل أكثر مما يخرج. |
| ACTIONABLE OPEN | نفس تعريف Executive، داخل نطاق الاختبارات المختارة. | حجم العمل المتبقي للفريق أو الاختبار. |
| NET ACTIONABLE REDUCTION | نفس معادلة صافي الحركة. | صافي تحسن الاختبار. |
| RESOLUTION RATE | نفس معادلة معدل الحل. | قراءة الحل مقابل العمل المتاح؛ قارن الأحجام مع النسبة. |

Improving + Increasing لا يلزم أن يساوي Tests in Scope؛ يوجد اختبارات بلا تغيير أو بلا عمل.

## Critical & Aging

مصدرها `CurrentClashes.csv` وآخر تصدير فقط، وليست إعادة بناء تاريخية حسب فترة Executive. الفلاتر تشمل الشدة وزوج التخصصات والاختبار والعمر؛ الأرقام خاصة بالسجلات Actionable فقط.

| الكارد / Measure | التعريف | استخدامه |
|---|---|---|
| ACTIONABLE OPEN / Current Open Count | عدد سجلات CurrentClashes ذات CoordinationState = Actionable. | قائمة العمل الحالية بعد الفلاتر. |
| CRITICAL · ACTIONABLE / Current Critical | Actionable المصنفة Critical. | الأولوية الحالية العالية حسب تصنيف الاختبار. |
| OPEN 14+ DAYS / Open 14 Plus | Actionable ذات AgeDays ≥ 14. | عمل قديم يحتاج مراجعة؛ 14 يومًا حد مراجعة وليس SLA تعاقديًا. |
| CRITICAL 14+ DAYS / Critical 14 Plus | Critical وActionable وعمرها ≥ 14. | اجتماع الخطورة والعمر يستدعي أولوية متابعة. |
| AVG AGE · DAYS / Average Age Days | المتوسط الحسابي لـ AgeDays بين السجلات Actionable المطابقة. | مؤشر عام قد ينخفض بدخول كلاشات جديدة كثيرة؛ لا يثبت وحده حل القديم. |
| OLDEST · DAYS / Oldest Age Days | أكبر AgeDays ضمن السجلات Actionable المطابقة. | أقدم حالة للمراجعة التفصيلية. |

العمر منذ FirstSeen في تاريخ التتبع المتاح وحتى آخر Snapshot، وليس منذ بدء المشروع بالضرورة، ولا منذ آخر Reopen. Refresh وحده لا يزيد العمر دون تصدير Tracker جديد. المتوسط والأقدم قد يظهران فارغين عند عدم وجود سجلات.

## Clash Details

| الكارد / Measure | التعريف | استخدامه |
|---|---|---|
| MATCHING CURRENT RECORDS / Current Total Count | كل سجلات CurrentClashes المطابقة للفلاتر بكل حالاتها. | حجم نتيجة البحث الحالية؛ ليس مرادفًا للمفتوح Actionable. |
| MATCHING CRITICAL RECORDS / Current Critical Total | السجلات المطابقة المصنفة Critical بكل حالاتها. | يشمل المقبول والمحلول المحتفظ به إذا لم تستبعده الفلاتر. |
| Detail Selection | GUID عند اختيار سجل واحد يحدد TestName وClashGuid. | اربط المناقشة بحالة محددة يمكن العثور عليها في Navisworks. |

تفاصيل Element A/B تعرض ID والاسم والـ Layer ومسار النموذج، بجانب الوصف والتعليقات، من حقول التصدير. Not provided يعني أن المعلومة غير متاحة في البيانات، وليس أن العنصر غير موجود. اختيار أكثر من كلاش لا يعطي تفاصيل عنصر موثوقة ولذلك تظهر رسالة طلب اختيار كلاش واحد.

## Resolution & Approvals

كروت الحل والقبول والعائد حركات خلال الفترة من `OperationalProgress.csv`. نُقل إلى هذه الصفحة أيضًا كارد Reviewed Current وكارد Resolved Retained؛ كلاهما رصيد عند لقطة النهاية، لا حركة خلال الفترة. Approval Revoked موجود بالفعل ولم نكرر إضافته.

| الكارد / Measure | التعريف | استخدامه |
|---|---|---|
| RESOLVED · PERIOD / Resolved All | جميع انتقالات الحل: بالـ Status + الاختفاء. | إجمالي الحل المرصود حتى لما كان الكلاش مقبولًا سابقًا. |
| BY RESOLVED STATUS / Resolved By Status | حل مرصود بسبب التحول إلى Resolved. | الحل يسجل فورًا حتى دون Compact. |
| BY DISAPPEARANCE / Resolved Disappeared | GUID غير محلول سابقًا اختفى من اللقطة التالية. | حل حسب قاعدة المشروع؛ تحقق من اكتمال التصدير قبل اعتباره إنجازًا. |
| APPROVED FROM OPEN / Actionable Approved | انتقال Actionable إلى Approved. | خروج من قائمة العمل بالقبول، لا بالحل الهندسي. |
| RETURNED TO ACTION / Returned to Action | رجوع GUID سابق إلى Actionable من حالة مقبولة أو مغلقة أو غياب سابق. | عمل عاد إلى القائمة؛ ليس بالضرورة رجوعًا من Resolved فقط. |
| APPROVAL REVOKED / Approval Revoked | انتقال Approved إلى حالة Actionable خلال الفترة؛ داخل Returned to Action بالفعل. | عمل عاد بعد إلغاء القبول؛ لا تضفه مرة أخرى. |

Resolved All = By Resolved Status + By Disappearance. أما مطابقة قائمة العمل فتستخدم **ResolvedFromActionable** فقط:

`Opening Actionable + New + Returned − ResolvedFromActionable − ApprovedFromActionable = Closing Actionable`

لا تخصم Resolved All من المفتوح دون تمييز ما كان Approved سابقًا. GUID واحد قد يغلق ويعود ويغلق، فتعد الحركات أحداثًا متعددة وليست دائمًا عدد كلاشات فريدة خلال الفترة.

### تفاصيل الأرصدة المنقولة من Executive Overview

| الكارد / Measure | التعريف | القراءة |
|---|---|---|
| REVIEWED CLASHES · STILL OPEN / Reviewed Current | رصيد Reviewed، وهو جزء من Actionable Open. | تمت مراجعتها ولكن المراجعة وحدها لا تقفلها. لا تضف الرقم إلى Open مرة ثانية. |
| RESOLVED RETAINED / Resolved Retained | سجلات حالتها Resolved وما زالت موجودة في التصدير. | اتحلت بالفعل ولم تُحذف بالـ Compact؛ لا تحتسب مفتوحة. |

### مثال توضيحي افتراضي

بدأنا بـ 1,000 Actionable، دخل 100 جديد وعاد 20، حُلّ 150 من المفتوح وقُبل 30: النهائي 940، وصافي الانخفاض 60، ومعدل الحل 150 ÷ 1,120 = 13.4%. لو اتحل أيضًا 10 كانوا Approved من قبل، يظهر Resolved All = 160 دون خصم العشرة مرة ثانية من قائمة العمل.

## Snapshot Quality

مصدرها `SnapshotQuality.csv`. العد حسب الاختبار واللقطة داخل الفترة، وليس عدد اختبارات فريدًا عبر كل التاريخ.

| الكارد / Measure | التعريف | كيف تتصرف؟ |
|---|---|---|
| QUALITY FLAGS / Quality Flags | مجموع HasAlert: صف اختبار/لقطة به قاعدة واحدة أو أكثر. | راجع أسباب الصفوف؛ نفس الاختبار قد يتكرر عبر اللقطات. |
| MISSING TESTS / Missing Tests | اختبار كان موجودًا في التصدير السابق وغاب من الحالي. | تحقق من النطاق والتصدير وإعادة التسمية. وجود اختبار بصفر كلاشات مختلف عن غياب الاختبار. |
| ADDED TESTS / Added Tests | اختبار ظهر ولم يكن موجودًا في التصدير السابق، باستثناء خط الأساس. | قد يكون نطاقًا جديدًا أو إعادة تسمية؛ ليس كلاشًا جديدًا بحد ذاته. |
| LARGE DROPS / Large Drops | انخفاض في إجمالي السجلات الفعلية ≥ 100 سجل و≥ 30% معًا، والاختبار موجود في اللقطتين. | قد يكون تقدمًا صحيحًا أو Compact أو تصديرًا ناقصًا؛ راجع قبل نسبة الانخفاض للحل. |
| UNKNOWN STATUS ROWS / Unknown Status Rows | مجموع الصفوف ذات حالة فارغة أو غير معروفة في اللقطات المحددة؛ قد يتكرر GUID. | صحح مصدر الحالة؛ تعامل هذه الصفوف كـ Actionable حتى لا تختفي من الشغل. |

Quality Flags ليس مجموع باقي الكروت: صف واحد قد يفعّل أكثر من قاعدة، وUnknown Status Rows يعد سجلات. التنبيه لا يغيّر أرقام الحل تلقائيًا. عدم التنبيه لا يضمن اكتمال البيانات.

## الفترات والعناوين والرسوم

| العنصر | القراءة الصحيحة |
|---|---|
| All history | التاريخ المتاح كله، مع أول لقطة كخط أساس لا كإنجاز. |
| Today / Last 7 days | اليوم الفعلي / اليوم والستة أيام السابقة، وليست آخر سبع لقطات. |
| Custom period | نطاق التاريخ يعمل عند اختيار هذا الوضع. |
| STOCK AS OF | تاريخ لقطة الرصيد المعروض؛ قد تسبق بداية الفترة عند عدم وجود تصدير جديد. |
| Period Caption | الفترة التي تجمع حركاتها. اختيارات الفترة مستقلة بين الصفحات وليست متزامنة. |
| AS OF في Aging/Details | آخر لقطة للبيانات الحالية؛ الصفحتان لا تعرضان رصيدًا تاريخيًا للفترة. |
| Quality Alerts Banner | ملخص جودة للفترة على نطاق كل الاختبارات. No export يعني حركات صفر ورصيد آخر معلوم، لا إثبات عدم حدوث عمل. |
| Selected Test Caption | اسم الاختبار عند اختيار واحد، وإلا ALL SELECTED TESTS. |
| Burndown / Open trend | رصيد Actionable لكل لقطة داخل الفترة. انخفاضه قد يأتي من حل أو قبول. |
| Daily movement (Executive) | New Inflow يجمع الجديد والعائد؛ Resolved Trend يخص الحل من Actionable، وApproved Trend يخص القبول منه. تجمع الحركات حسب اليوم، بينما يظل Open Trend لكل لقطة دون تجميع أرصدة اليوم. |
| Top Clash Tests | أعلى عشرة اختبارات في Actionable بنهاية الفترة داخل التحديد. |
| Severity / Discipline Pair | توزيع الرصيد حسب تصنيف الاختبار وزوج التخصصات؛ ليس عدد عناصر BIM فريدة. |
| Age distribution | توزيع أعمار العمل الحالي؛ اربطه بجدول أقدم الحالات. |
| Reconciliation table | افتتاحي + جديد + عائد − محلول من المفتوح − مقبول من المفتوح = ختامي. |

## ترتيب مقترح للاجتماع

1. أعلن الفترة ووقت Snapshot والنطاق، ثم راجع Snapshot Quality.
2. ابدأ بـ Actionable Open، ثم افصل الجديد والعائد والحل والقبول، واستنتج Net Reduction.
3. افتح الاختبارات المتزايدة وأعلى أحجام العمل، وعيّن مسؤولًا وخطوة وموعد متابعة.
4. راجع Critical و14+، ثم استخدم Clash Details لتحديد العناصر والحالة.
5. راجع Approvals وإلغاءات القبول، واعرض Forecast كتقدير مشروط فقط.

نسختا Dark وLight تستخدمان نفس النموذج والمعادلات، والاختلاف بصري. لا توجد صفحات Help المرفوضة أو روابطها؛ هذا الدليل خارج التقرير.

## تحديث 1.2: مراجعات الجودة

Quality Flags يحتفظ بالتنبيهات الأصلية. Action Required يعرض ما لا يزال يحتاج إجراء، وAccepted Reviews يعرض المقبول، وNeeds Correction يعرض المطلوب تصحيحه. التوقع يستخدم RequiresAction في نافذته بدل العدد الخام للتنبيهات؛ باقي شروطه لم تتغير. التفاصيل في [دليل مراجعة الجودة](quality-reviews.ar.md).


## تحديث 1.7: وضوح Executive Overview — 28 سبتمبر 2026

تحديث للداشبورد فقط؛ نسخة Tracker تظل 1.6. لم يتغير منطق المقاييس القديمة أو توقع النهاية. الصف الأول يعرض Actionable Open، New، Resolved All، صافي الانخفاض شامل القبول، صافي الانخفاض بدون القبول، ثم Forecast بحجمه السابق أو أصغر. الصف الثاني يعرض Total Current، Approved Current، Resolution Rate، تغير الرصيد خلال نافذة السبعة أيام، وNew Approvals. تفاصيل Reviewed clashes وResolved Retained وApproval Revoked تُقرأ في Resolution & Approvals.

كل أعداد الكروت معروضة كاملة بفواصل آلاف، ومعدل الحل بمنزلة عشرية واحدة. القبول بنفسجي منفصل عن لون العمل الجديد؛ Critical أحمر، Medium كهرماني، Low أزرق. Severity مشتق من اسم الاختبار وليس تقييمًا هندسيًا مستقلًا. أعلى عشرة اختبارات أصبحت جدولًا بمساحة أوسع للأسماء بدل أعمدة أفقية ذات أسماء مختصرة.

| المقياس/التعليق الجديد | التعريف الدقيق |
|---|---|
| Net Reduction Split Caption | Resolved-driven = Actionable Resolved − New Actionable − Returned to Action؛ Approval-driven = Actionable Approved. مجموع الجزأين يساوي Net Actionable Reduction. الجزء الأول قد يكون سالبًا، وما زال يشمل الحل بالاختفاء. |
| Resolved Split Caption | يقسم Resolved All إلى Resolved By Status وResolved Disappeared، مع فواصل آلاف. اختفاء GUID وفق قاعدة المشروع لا يثبت اكتمال التصدير. |
| Actionable Comparison Snapshot 7d | أحدث لقطة عند أو قبل Selected Snapshot ناقص 7 أيام؛ عند عدم وجودها تستخدم أقدم لقطة أقدم من المختارة. لا لقطة مقارنة مع تاريخ فارغ أو لقطة وحيدة. |
| Actionable Change 7d Caption | يبين تاريخ ووقت المرجع والفرق الفعلي بالأيام. Short history يعني أقل من 7 أيام؛ Snapshot gap يعني أن أقرب لقطة صالحة أقدم من النافذة المستهدفة. |
| Actionable Change 7d Color / Light | زيادة الرصيد أحمر، انخفاضه أخضر، الصفر أو غياب المقارنة محايد. |
| Net Reduction excl Approvals Color / Light | انخفاض صافي سلبي أحمر، وغير السالب أخضر. |
| Custom Period Title، Custom Period Color / Light | عنوان ACTIVE ولون مميز فقط مع Custom period؛ غير ذلك INACTIVE ولون مكتوم. التحكم لا يختفي ولا يتعطل فعليًا، لكن قيمته لا تدخل الحسابات إلا في الوضع المخصص. |
| Quality Alerts Banner | نفس قواعد الرسالة القديمة مع تسمية quality alerts صراحة. لا علاقة لها بعدد الكلاشات ذات Status = Reviewed. |

**توحيد أزواج التخصصات:** جدول Tests يوحّد DisciplinePair بترتيب الاسمين أبجديًا بعد إزالة المسافات الطرفية وتحويلهما إلى حروف كبيرة؛ AR vs PL وPL vs AR يصبحان AR vs PL. DisciplineA وDisciplineB لم يتغيرا. تم التوحيد داخل Power Query للموديل؛ CSV القديمة لم تُكتب من جديد. السلايسرز تعتمد الحقل نفسه، بما فيها سلايسر الـ3D دون تغيير ملفات تلك الصفحة. اتجاه A/B داخل تفاصيل العنصر يظل محفوظًا. إجمالي الكلاشات لا يتغير.

**الفوركاست:** عنوانه يذكر أنه يشمل القبول؛ لا يعتبر موعد إغلاق هندسي خالص. Engineering-only forecast متابعة مستقبلية، ولم يُنفذ في هذا التحديث.

**مثال من البيانات المحلية وقت التحقق:** كل التاريخ يعطي صافي انخفاض 11,508 = 4,819 بدون القبول + 6,689 أثر القبول. Resolved All = 24,426 = 1,573 بالحالة + 22,853 بالاختفاء. الرصيد في 28 سبتمبر 13:06:05 هو 24,189 مقابل 22,568 في 21 سبتمبر 11:44:17؛ التغير +1,621. هذه نتائج وقتية وليست قيمًا ثابتة للكروت.

تم تشغيل البناء و280 assertion للأداة، وفحوص JSON والعلاقات وربط الحقول والتطابق بين تخطيطَي الصفحتين المعدلتين، وفحص حسابي من CSV. لم نفتح Power BI Desktop لهذا التحديث؛ تنفيذ DAX وPower Query ومظهر النصوص والألوان يحتاجان فحصًا داخله قبل الدمج. راجع [نتائج المراجعة وقائمة الفحص](executive-review-2026-09-28.md) و[نص DAX الجديد بالكامل](executive-review-new-measures.dax).
