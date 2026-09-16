# دليل قراءة HUMAIN Coordination Dashboard

مرجع مطابق لتعريفات الـ Measures وتقارير Dark وLight المرفقة، بتاريخ 16 سبتمبر 2026. الأسماء التقنية بين الأقواس تسمح بمراجعة المعادلات في `powerbi/HUMAIN.SemanticModel/model.bim`.

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
| NET ACTIONABLE REDUCTION / Net Reduction | ResolvedFromActionable + ApprovedFromActionable − NewActionable − ReturnedActionable. | الموجب تحسن صافٍ، السالب زيادة صافية، والصفر قد يخفي حركة كبيرة متعادلة. القبول يساهم في الانخفاض. |
| RESOLUTION RATE / Resolution Rate | ActionableResolved ÷ (ActionablePrevious + NewActionable + Returned to Action). تظهر فارغة إذا المقام صفر. | نسبة ما حُلّ من العمل المتاح للفترة؛ Approved لا يدخل البسط، وليست Resolved All ÷ Open الحالي. |
| FORECAST · ACTIONABLE / Forecast Display | نهاية تقديرية = تاريخ آخر Snapshot + تقريب لأعلى لـ Actionable ÷ متوسط صافي الانخفاض اليومي. المتوسط يستخدم الفواصل المرصودة المنتهية في آخر 7 أيام ويقسم على مجموع مدتها الفعلية. | توقع نفاد قائمة العمل بنفس المعدل، ويشمل أثر Approved؛ ليس موعد تسليم معتمدًا. |
| TOTAL CURRENT / Total Current | كل السجلات الموجودة فعليًا في Snapshot النهاية، بكل الحالات. | حجم سجل التصدير: Actionable + Approved + Resolved Retained. |
| APPROVED · ACCEPTED / Approved Current | رصيد السجلات الموجودة حاليًا بحالة Approved. | تعارضات مقبولة تحتاج أن يكون قبولها مبررًا؛ ليست حلولًا هندسية. |
| REVIEWED · STILL OPEN / Reviewed Current | رصيد Reviewed، وهو جزء من Actionable Open. | تمت مراجعتها ولكن المراجعة وحدها لا تقفلها. لا تضف الرقم إلى Open مرة ثانية. |
| RESOLVED RETAINED / Resolved Retained | سجلات حالتها Resolved وما زالت موجودة في التصدير. | اتحلت بالفعل ولم تُحذف بالـ Compact؛ لا تحتسب مفتوحة. |
| NEW APPROVALS / New Approvals | انتقالات مرصودة إلى Approved خلال الفترة؛ أول ظهور Approved ليس حركة قبول. | حجم تغييرات القبول في الفترة، وليس رصيد Approved الحالي. |
| APPROVAL REVOKED / Approval Revoked | انتقال Approved إلى حالة Actionable خلال الفترة. | عمل عاد بعد إلغاء القبول؛ داخل Returned to Action بالفعل، فلا تضفه إليه مرة ثانية. |

### رسائل Forecast

| الرسالة | معناها |
|---|---|
| No snapshot | لا توجد لقطة قبل نهاية الفترة. |
| Review data | توجد تنبيهات جودة في نافذة التوقع الأخيرة؛ التوقع محجوب حتى مراجعة البيانات. |
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

كل الكروت التالية حركات خلال الفترة من `OperationalProgress.csv`.

| الكارد / Measure | التعريف | استخدامه |
|---|---|---|
| RESOLVED · PERIOD / Resolved All | جميع انتقالات الحل: بالـ Status + الاختفاء. | إجمالي الحل المرصود حتى لما كان الكلاش مقبولًا سابقًا. |
| BY RESOLVED STATUS / Resolved By Status | حل مرصود بسبب التحول إلى Resolved. | الحل يسجل فورًا حتى دون Compact. |
| BY DISAPPEARANCE / Resolved Disappeared | GUID غير محلول سابقًا اختفى من اللقطة التالية. | حل حسب قاعدة المشروع؛ تحقق من اكتمال التصدير قبل اعتباره إنجازًا. |
| APPROVED FROM OPEN / Actionable Approved | انتقال Actionable إلى Approved. | خروج من قائمة العمل بالقبول، لا بالحل الهندسي. |
| APPROVAL REVOKED / Approval Revoked | انتقال Approved إلى Actionable. | إلغاء قبول، وهو جزء من Returned. |
| RETURNED TO ACTION / Returned to Action | رجوع GUID سابق إلى Actionable من حالة مقبولة أو مغلقة أو غياب سابق. | عمل عاد إلى القائمة؛ ليس بالضرورة رجوعًا من Resolved فقط. |

Resolved All = By Resolved Status + By Disappearance. أما مطابقة قائمة العمل فتستخدم **ResolvedFromActionable** فقط:

`Opening Actionable + New + Returned − ResolvedFromActionable − ApprovedFromActionable = Closing Actionable`

لا تخصم Resolved All من المفتوح دون تمييز ما كان Approved سابقًا. GUID واحد قد يغلق ويعود ويغلق، فتعد الحركات أحداثًا متعددة وليست دائمًا عدد كلاشات فريدة خلال الفترة.

### مثال توضيحي افتراضي

بدأنا بـ 1,000 Actionable، دخل 100 جديد وعاد 20، حُلّ 150 من المفتوح وقُبل 30: النهائي 940، وصافي الانخفاض 60، ومعدل الحل 150 ÷ 1,120 = 13.39%. لو اتحل أيضًا 10 كانوا Approved من قبل، يظهر Resolved All = 160 دون خصم العشرة مرة ثانية من قائمة العمل.

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
| Quality Banner | ملخص جودة للفترة على نطاق كل الاختبارات. No export يعني حركات صفر ورصيد آخر معلوم، لا إثبات عدم حدوث عمل. |
| Selected Test Caption | اسم الاختبار عند اختيار واحد، وإلا ALL SELECTED TESTS. |
| Burndown / Open trend | رصيد Actionable لكل لقطة داخل الفترة. انخفاضه قد يأتي من حل أو قبول. |
| New / Resolved trend | New Inflow يجمع الجديد والعائد؛ Resolved Trend يخص الحل من Actionable، وApproved Trend يخص القبول منه. |
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
