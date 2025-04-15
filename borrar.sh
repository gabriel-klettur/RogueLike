Beelzebot
beelzebot_62819
Sharing their screen

Beelzebot — 4/10/2025 8:51 PM
https://www.spritefusion.com/tilesets
https://www.spritefusion.com/
Sprite Fusion, Free 2D tilemap editor
A simple, free tilemap Editor Online - Sprite Fusion
Design your tilemap with Sprite Fusion, the ultimate free online level editor. Design 2D games with ease, direclty export to Unity Tilemap and Godot Scenes. Ideal for retro games!
A simple, free tilemap Editor Online - Sprite Fusion
Beelzebot — 4/12/2025 2:06 PM
princios SOLID
MiSTiC — 12:27 PM
#!/bin/bash

CURRENT_PATH="`dirname \"$0\"`"

source /usr/local/nagios/.bash_profile
source $CURRENT_PATH/ConnectionChain

DATABASE=$CURRENT_PATH/EODC_ACQUISITIONS.db

SQLITE=/usr/bin/sqlite3

####Outfiles
logfile=$CURRENT_PATH/main_process.log
current_acquisitions_outfile=$CURRENT_PATH/current_acquisitions.out
current_acquisitions_provider_outfile=$CURRENT_PATH/current_acquisitions_provider.out

log_message() {
    local message="$1"
    local logfile="${2:-$logfile}"

    # Get the current date and time in the desired format
    local timestamp=$(date +"%Y%m%d %H:%M:%S")

    # Write the timestamp and message to the logfile
    echo "$timestamp: $message" >> "$logfile"

        # Echo message to terminal
        echo "$timestamp: $message"
}

check_sensor_status ()
{

# # # This function checks the status of the stored result given the Image ID and the Column name

# # # ARGUMENTS: Image ID, Sensor [CSN_SMON_10, CSN_SMON_20...]

# # # The function will return a 0 in case the result is "OK" or "N/A" or a 1 if it finds anything else (CRITICAL or NULL are the other options)

imageid=$1
database_column=$2

query_result=`$SQLITE $DATABASE <<EOF
select "$database_column" from acquisitions where service_id="$imageid";
EOF`

log_message "Retrieved value $query_result for column $database_column on database"

#echo "Query result: $query_result"

ret=$?
if [ $ret -eq 0 ]
then
        if [ "$query_result" == "OK" ] ||  [ "$query_result" == "N/A" ]
        then
                return 0
        else
                return 1
        fi

else
        log_message "Error $ret getting "$database_column" status from database for image $imageid"
        return 1
fi

}

database_creation () {

#Create Initial Database Structure if file doesn't exists. The "active" field is used for the IDs retrieved by the function get_current_acquisitions_provider, the rest should be set to active=0 in the function set_acquisitions_to_inactive

if [ ! -f "$DATABASE" ]; then

        log_message "Database $DATABASE doesn't exist, creating it" "$logfile"

    $SQLITE $DATABASE <<EOF
CREATE TABLE acquisitions (
  service_id INTEGER PRIMARY KEY,
  date TEXT NOT NULL,
  projects TEXT NOT NULL,
  provider TEXT NOT NULL,
  CSN_SMON_10 TEXT,
  CSN_SMON_20 TEXT,
  CSN_SMON_60 TEXT,
  CSN_SMON_80 TEXT,
  CSN_SMON_90 TEXT,
  CSN_SMON_100 TEXT,
  jira_id TEXT,
  active BOOLEAN NOT NULL DEFAULT 1);
EOF
fi

        ret=$?

        if [ $ret -eq 0 ]
        then
                log_message "Database $DATABASE created" "$logfile"
        else
                log_message "Error $(ret) creating database $DATABASE" "$logfile"
        fi
... (798 lines left)
Collapse
script.sh
34 KB
﻿
#!/bin/bash

CURRENT_PATH="`dirname \"$0\"`"

source /usr/local/nagios/.bash_profile
source $CURRENT_PATH/ConnectionChain

DATABASE=$CURRENT_PATH/EODC_ACQUISITIONS.db

SQLITE=/usr/bin/sqlite3

####Outfiles
logfile=$CURRENT_PATH/main_process.log
current_acquisitions_outfile=$CURRENT_PATH/current_acquisitions.out
current_acquisitions_provider_outfile=$CURRENT_PATH/current_acquisitions_provider.out

log_message() {
    local message="$1"
    local logfile="${2:-$logfile}"

    # Get the current date and time in the desired format
    local timestamp=$(date +"%Y%m%d %H:%M:%S")

    # Write the timestamp and message to the logfile
    echo "$timestamp: $message" >> "$logfile"

        # Echo message to terminal
        echo "$timestamp: $message"
}

check_sensor_status ()
{

# # # This function checks the status of the stored result given the Image ID and the Column name

# # # ARGUMENTS: Image ID, Sensor [CSN_SMON_10, CSN_SMON_20...]

# # # The function will return a 0 in case the result is "OK" or "N/A" or a 1 if it finds anything else (CRITICAL or NULL are the other options)

imageid=$1
database_column=$2

query_result=`$SQLITE $DATABASE <<EOF
select "$database_column" from acquisitions where service_id="$imageid";
EOF`

log_message "Retrieved value $query_result for column $database_column on database"

#echo "Query result: $query_result"

ret=$?
if [ $ret -eq 0 ]
then
        if [ "$query_result" == "OK" ] ||  [ "$query_result" == "N/A" ]
        then
                return 0
        else
                return 1
        fi

else
        log_message "Error $ret getting "$database_column" status from database for image $imageid"
        return 1
fi

}

database_creation () {

#Create Initial Database Structure if file doesn't exists. The "active" field is used for the IDs retrieved by the function get_current_acquisitions_provider, the rest should be set to active=0 in the function set_acquisitions_to_inactive

if [ ! -f "$DATABASE" ]; then

        log_message "Database $DATABASE doesn't exist, creating it" "$logfile"

    $SQLITE $DATABASE <<EOF
CREATE TABLE acquisitions (
  service_id INTEGER PRIMARY KEY,
  date TEXT NOT NULL,
  projects TEXT NOT NULL,
  provider TEXT NOT NULL,
  CSN_SMON_10 TEXT,
  CSN_SMON_20 TEXT,
  CSN_SMON_60 TEXT,
  CSN_SMON_80 TEXT,
  CSN_SMON_90 TEXT,
  CSN_SMON_100 TEXT,
  jira_id TEXT,
  active BOOLEAN NOT NULL DEFAULT 1);
EOF
fi

        ret=$?

        if [ $ret -eq 0 ]
        then
                log_message "Database $DATABASE created" "$logfile"
        else
                log_message "Error $(ret) creating database $DATABASE" "$logfile"
        fi

#sqlite3 /path/to/your/database <<EOF
#CREATE TABLE IF NOT EXISTS acquisitions (service_id INTEGER PRIMARY KEY, date TEXT, projects TEXT, CSN_SMON_10 TEXT, CSN_SMON_20 TEXT, CSN_SMON_60 TEXT, CSN_SMON_80 TEXT, CSN_SMON_90 TEXT, CSN_SMON_100 TEXT, active INTEGER);
#EOF

}

get_current_acquisitions () {
#This function is probably not needed, as the function get_current_acquisitions_provider gets the same data + an additional field, but it is keeped in the code for historical reasons. The call is disabled on the script

# # # This function will execute a query on the database to get the current acquisitions and store the result on the file of the given argument
# # # ARGUMENTS: outfile - the file where the results of the query will be stored
# # # The query will give the following type of output:

# # # -DATA-, ID_ORDER_DETAIL, ACQUISITION_START_DATE, PROJECTS

# # # Example lines:
# # # -DATA-      2409030051 2024-09-03 10:08:47.171   FRONTEX
# # # -DATA-      2409030032 2024-09-03 10:08:41.009   IMS_LAW

local outfile="$1"

log_message "Getting list of current acquisitions"

sqlplus -s $DB_CREDENTIALS << EOF > /dev/null
set pagesize 0
set trimspool on
set headsep off
set echo off
set feedback off
set heading off
set verify off
set linesize 200
set term off
set head off

column DATA format a6
column ACQUISITION_START_DATE format a25
column PROJECTS format a50

spool $outfile

SELECT DISTINCT
'-DATA-' as DATA,
ID_ORDER_DETAIL, ACQUISITION_START_DATE, PROJECTS
FROM PORUSR.ORDER_DETAILS, (SELECT ORDER_DETAIL_ID, LISTAGG(PROJECT, ',') WITHIN GROUP (ORDER BY PROJECT) AS PROJECTS
                            FROM PORUSR.X_ORDER_DETAILS_PROJECTS, PORUSR.SIB_PROJECTS
                            WHERE X_ORDER_DETAILS_PROJECTS.PROJECT_ID = SIB_PROJECTS.ID_PROJECT
                            GROUP BY ORDER_DETAIL_ID) PROJECTS_TAB, PORUSR.SIB_SERVICE_TYPES
WHERE TO_TIMESTAMP (ACQUISITION_START_DATE, 'YYYY-MM-DD HH24:MI:SS.FF') >= SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '24' HOUR - (MAX_DOWNLINK_DELAY/1440 + (MAX_DELIVERY_DELAY_SP)/24 + MAX_DELIVERY_DELAY_SO/24)
AND TO_TIMESTAMP (ACQUISITION_START_DATE, 'YYYY-MM-DD HH24:MI:SS.FF') <= SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '90' MINUTE - (MAX_DOWNLINK_DELAY/1440 + MAX_DELIVERY_DELAY_SP/24 + MAX_DELIVERY_DELAY_SO/24)
AND ORDER_DETAIL_STATUS_ID IN (9,11,12,14)
AND ORDER_DETAILS.ID_ORDER_DETAIL = PROJECTS_TAB.ORDER_DETAIL_ID
and SERVICE_TYPE_ID = ID_SERVICE_TYPE
and sensor_type <> 'OPTICAL'
 union all
 SELECT DISTINCT
 '-DATA-' as DATA,
 ID_ORDER_DETAIL, ACQUISITION_START_DATE, PROJECTS
FROM PORUSR.ORDER_DETAILS, (SELECT distinct ORDER_DETAIL_ID, LISTAGG(PROJECTS, ',') WITHIN GROUP (ORDER BY PROJECTS) AS PROJECTS
from (SELECT distinct ORDER_DETAIL_ID, PROJECT AS PROJECTS
                            FROM PORUSR.X_ORDER_DETAILS_PROJECTS, PORUSR.SIB_PROJECTS
                            WHERE X_ORDER_DETAILS_PROJECTS.PROJECT_ID = SIB_PROJECTS.ID_PROJECT)
                            GROUP BY ORDER_DETAIL_ID) PROJECTS_TAB, PORUSR.SIB_SERVICE_TYPES
WHERE TO_TIMESTAMP (ACQUISITION_START_DATE, 'YYYY-MM-DD HH24:MI:SS.FF') >= SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '24' HOUR - (MAX_DOWNLINK_DELAY/1440 + (MAX_DELIVERY_DELAY_SP)/24 + MAX_DELIVERY_DELAY_SO/24)
AND TO_TIMESTAMP (ACQUISITION_START_DATE, 'YYYY-MM-DD HH24:MI:SS.FF') <= SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '90' MINUTE - (MAX_DOWNLINK_DELAY/1440 + MAX_DELIVERY_DELAY_SP/24 + MAX_DELIVERY_DELAY_SO/24)
AND ORDER_DETAIL_STATUS_ID IN (9,11,12,14)
AND ORDER_DETAILS.ID_ORDER_DETAIL = PROJECTS_TAB.ORDER_DETAIL_ID
and SERVICE_TYPE_ID = ID_SERVICE_TYPE
and sensor_type = 'OPTICAL'
and ACQUISITION_START_DATE in (select max(ACQUISITION_START_DATE) from PORUSR.ORDER_DETAILS where sensor_type = 'OPTICAL' and ORDER_DETAIL_STATUS_ID IN (9,11,12,14) group by ID_ORDER_DETAIL)
       ORDER BY ACQUISITION_START_DATE DESC;

spool off
exit
EOF

        ret=$?

        if [ $ret -eq 0 ]
        then
                log_message "File $outfile updated with current acquisitions"
        else
                log_message "Error $ret while getting current acquisitions file $outfile"
        fi


}

get_current_acquisitions_provider() {

# # # This function will execute a query on the database to get the current acquisitions and store the result on the file of the given argument
# # # ARGUMENTS: outfile - the file where the results of the query will be stored

# # # The query will give the following type of output:

# # # ID_ORDER_DETAIL, ACQUISITION_START_DATE, PROJECTS, PROVIDER

# # # Example lines:
# # #    2409030051|2024-09-03 10:08:47.171  |FRONTEX                                           |AIRBUSEUSI-OPT-S
# # #    2409030032|2024-09-03 10:08:41.009  |IMS_LAW                                           |KSAT-S

local outfile="$1"

log_message "Getting list of current acquisitions with provider"

sqlplus -s $DB_CREDENTIALS << EOF > /dev/null

set pagesize 0
set trimspool on
set headsep off
set echo off
set feedback off
set heading off
set verify off
set linesize 200
set term off
set head off
set colsep "|"
set trimout on

column ACQUISITION_START_DATE format a25
column PROJECTS format a50
column PROVIDER format a25

spool $outfile

SELECT DISTINCT ID_ORDER_DETAIL, ACQUISITION_START_DATE, PROJECTS, su.USER_ACCOUNT AS PROVIDER
FROM PORUSR.ORDER_DETAILS od
JOIN PORUSR.X_ORDER_DETAILS_USERS xodu ON xodu.ORDER_DETAIL_ID = od.ID_ORDER_DETAIL
JOIN PORUSR.SIB_USERS su ON xodu.USER_ID = su.ID_USER
INNER JOIN PORUSR.SIB_X_USERS_ROLES sxur ON sxur.user_id=su.id_user AND sxur.role_id=11, (SELECT ORDER_DETAIL_ID, LISTAGG(PROJECT, ',') WITHIN GROUP (ORDER BY PROJECT) AS PROJECTS
                            FROM PORUSR.X_ORDER_DETAILS_PROJECTS, PORUSR.SIB_PROJECTS
                            WHERE X_ORDER_DETAILS_PROJECTS.PROJECT_ID = SIB_PROJECTS.ID_PROJECT
                            GROUP BY ORDER_DETAIL_ID) PROJECTS_TAB, PORUSR.SIB_SERVICE_TYPES
WHERE TO_TIMESTAMP (ACQUISITION_START_DATE, 'YYYY-MM-DD HH24:MI:SS.FF') >= SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '24' HOUR - (MAX_DOWNLINK_DELAY/1440 + (MAX_DELIVERY_DELAY_SP)/24 + MAX_DELIVERY_DELAY_SO/24)
AND TO_TIMESTAMP (ACQUISITION_START_DATE, 'YYYY-MM-DD HH24:MI:SS.FF') <= SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '90' MINUTE - (MAX_DOWNLINK_DELAY/1440 + MAX_DELIVERY_DELAY_SP/24 + MAX_DELIVERY_DELAY_SO/24)
AND ORDER_DETAIL_STATUS_ID IN (9,11,12,14)
AND od.ID_ORDER_DETAIL = PROJECTS_TAB.ORDER_DETAIL_ID
and SERVICE_TYPE_ID = ID_SERVICE_TYPE
AND xodu.LAST_STATUS_ID <> 3
and sensor_type <> 'OPTICAL'
 union all
 SELECT DISTINCT ID_ORDER_DETAIL, ACQUISITION_START_DATE, PROJECTS, su.USER_ACCOUNT AS PROVIDER
FROM PORUSR.ORDER_DETAILS od
JOIN PORUSR.X_ORDER_DETAILS_USERS xodu ON xodu.ORDER_DETAIL_ID = od.ID_ORDER_DETAIL
JOIN PORUSR.SIB_USERS su ON xodu.USER_ID = su.ID_USER
INNER JOIN PORUSR.SIB_X_USERS_ROLES sxur ON sxur.user_id=su.id_user AND sxur.role_id=11, (SELECT distinct ORDER_DETAIL_ID, LISTAGG(PROJECTS, ',') WITHIN GROUP (ORDER BY PROJECTS) AS PROJECTS
from (SELECT distinct ORDER_DETAIL_ID, PROJECT AS PROJECTS
                            FROM PORUSR.X_ORDER_DETAILS_PROJECTS, PORUSR.SIB_PROJECTS
                            WHERE X_ORDER_DETAILS_PROJECTS.PROJECT_ID = SIB_PROJECTS.ID_PROJECT)
                            GROUP BY ORDER_DETAIL_ID) PROJECTS_TAB, PORUSR.SIB_SERVICE_TYPES
WHERE TO_TIMESTAMP (ACQUISITION_START_DATE, 'YYYY-MM-DD HH24:MI:SS.FF') >= SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '24' HOUR - (MAX_DOWNLINK_DELAY/1440 + (MAX_DELIVERY_DELAY_SP)/24 + MAX_DELIVERY_DELAY_SO/24)
AND TO_TIMESTAMP (ACQUISITION_START_DATE, 'YYYY-MM-DD HH24:MI:SS.FF') <= SYS_EXTRACT_UTC(SYSTIMESTAMP) - INTERVAL '90' MINUTE - (MAX_DOWNLINK_DELAY/1440 + MAX_DELIVERY_DELAY_SP/24 + MAX_DELIVERY_DELAY_SO/24)
AND ORDER_DETAIL_STATUS_ID IN (9,11,12,14)
AND od.ID_ORDER_DETAIL = PROJECTS_TAB.ORDER_DETAIL_ID
and SERVICE_TYPE_ID = ID_SERVICE_TYPE
AND xodu.LAST_STATUS_ID <> 3
and sensor_type = 'OPTICAL'
and ACQUISITION_START_DATE in (select max(ACQUISITION_START_DATE) from PORUSR.ORDER_DETAILS where sensor_type = 'OPTICAL' and ORDER_DETAIL_STATUS_ID IN (9,11,12,14) group by ID_ORDER_DETAIL)
       ORDER BY ACQUISITION_START_DATE DESC;


spool off
exit
EOF

        ret=$?

        if [ $ret -eq 0 ]
        then
                log_message "File $outfile updated with current acquisitions with provider"
        else
                log_message "Error $ret while getting current acquisitions from database to $outfile"
        fi
}

set_acquisitions_to_inactive () {
# # # This function sets on the sqlite database all the images active to inactive, so that on the function insert_acquisitions_provider_data_into_database only the retrieved from the query from get_current_acquisitions_provider are active.

$SQLITE $DATABASE <<EOF
update acquisitions set active=0 where active=1;
EOF

ret=$?
if [ $ret -eq 0 ]
then
        log_message "All acquisitions set to inactive" "$logfile"
else
        log_message "Couldn't set the acquisitions as inactive" "$logfile"
fi
}

insert_acquisitions_provider_data_into_database () {

# # # This function reads the columns writen from the function get_current_acquisitions_provider from file $current_acquisitions_provider_outfile and insert the fields service_id, date, projects and providers into the database.
# # # Argument: infile

infile=$1

num_acquisitions=$(/usr/bin/wc -l $infile | /usr/bin/awk '{print $1}')
log_message "Reading file $infile with $num_acquisitions lines"

#Process acquisition file and insert into database
while IFS="|" read -r id date projects provider; do

#Modify the date format as the following example from 2024-09-04 07:27:14.674 to 2024-09-04 07:27:14
date=$(echo $date | /usr/bin/awk -F "." '{print $1}')

#Cleaning whitespaces for projects column. If not, it stores the projects followed by spaces until reaching 50 characters.
projects=$(echo $projects | tr -d ' ')
#Cleaning whitespaces for ImageID. If not, it stores the string with some spaces that may cause issues with the queries.
id=$(echo $id | tr -d '[:space:]')

$SQLITE $DATABASE <<EOF
INSERT OR IGNORE INTO acquisitions (service_id, date, projects, provider) VALUES ("$id", "$date", "$projects", "$provider");
UPDATE acquisitions set active=1 where service_id = "$id";
EOF


update_CSN_SMON_10_status "$id"

update_CSN_SMON_20_status "$id"

update_CSN_SMON_60_dummy "$id"

update_CSN_SMON_80_status "$id"

update_CSN_SMON_90_status "$id"

update_CSN_SMON_100_status "$id"


done < "$infile"

ret=$?
if [ $ret -eq 0 ]
then
        log_message "Database $DATABASE updated with current acquisitions" "$logfile"
else
        log_message "Error $ret updating database $DATABASE with current acquisitions" "$logfile"
fi



}


update_CSN_SMON_10_status () {
# # # This function first checks the status of the CSN_SMON_10 table on the database and if it is on critical or empty, it tries to update the status on the sqlite database.
# # # Argument: Image ID

#The query will output the IMAGEID and the ORDER_DETAIL_STATUS

#Example output:
#2409050028|                11

imageid=$1



#Si no comentamos el > /dev/null, no se puede obtener el output en la variable
#output=`sqlplus -s $DB_CREDENTIALS << EOF > /dev/null



#Check if the current status is OK or N/A. If the function returns a 0, no need to check again.
check_sensor_status "$imageid" "CSN_SMON_10"

CSN_SMON_10=$?


if [ "$CSN_SMON_10" == "1" ]
then
        sleep 1
        log_message "CSN_SMON_10 needs and update for image $imageid, retrieving data" "$logfile"

        output=`sqlplus -s $DB_CREDENTIALS << EOF

        set pagesize 0
        set trimspool on
        set headsep off
        set echo off
        set feedback off
        set heading off
        set verify off
        set linesize 200
        set term off
        set head off
        set colsep "|"


        SELECT
        ID_ORDER_DETAIL, ORDER_DETAIL_STATUS_ID
        FROM PORUSR.ORDER_DETAILS
        WHERE ID_ORDER_DETAIL = $imageid
        and ORDER_DETAIL_STATUS_ID = 11;

        exit
EOF`


        ret=$?
        if [ $ret -eq 0 ]
        then
                log_message "Got the following output from CSN_SMON_10 query: $output" "$logfile"

        else
                log_message "Error $ret while querying status for CSN_SMON_10" "$logfile"

        fi

        value=`echo $output | awk '{print $2}'`


        if [ "$value" == "11" ]
        then
                log_message "Setting CSN_SMON_10 as OK for image $imageid" "$logfile"
                state="OK"
        else
                log_message "Setting CSN_SMON_10 as CRITICAL for image $imageid" "$logfile"
                state="CRITICAL"
        fi

        #Update the database with the value
        $SQLITE $DATABASE <<EOF
        update acquisitions set CSN_SMON_10="$state" where service_id="$imageid";
EOF
        ret=$?
        if [ $ret -eq 0 ]
        then
                log_message "Updated SQLITE DB: update acquisitions set CSN_SMON_10=$state where service_id=$imageid;" "$logfile"
        else
                log_message "Error $ret while updating state $state for CSN_SMON_10 column on image $imageid " "$logfile"
        fi

else
        log_message "Skipping check of CSN_SMON_10 for image $imageid as is already on OK or N/A status"
fi

}

update_CSN_SMON_20_status () {

        imageid=$1

        check_sensor_status "$imageid" "CSN_SMON_20"

        CSN_SMON_20=$?


        if [ "$CSN_SMON_20" == "1" ]
        then
                sleep 1
                output=`sqlplus -s $DB_CREDENTIALS << EOF


                SELECT '-DATA-',sesp.service_element as serviceproviderservicetypes
                FROM porusr.order_details od
                INNER JOIN porusr.sib_service_types st on od.service_type_id=st.id_service_type
                INNER JOIN porusr.sib_service_elements sesp on (st.service_element_id_list_sp like TO_CHAR(sesp.id_service_element) ||',%' or
                st.service_element_id_list_sp like '%,'|| TO_CHAR(sesp.id_service_element) ||',%')
                WHERE
                od.id_order_detail = $imageid and
                od.order_detail_status_id = 11 and
                sesp.service_element!='Quality Report'
                AND (CASE ID_SERVICE_ELEMENT WHEN 1 THEN 'LIC' WHEN 2 THEN case when sensor_type = 'OPTICAL' then 'EOPO' when sensor_type <> 'OPTICAL' then 'EOP' END WHEN 3 THEN case when sensor_type = 'OPTICAL' then 'EOPO' when sensor_type <> 'OPTICAL' then 'EOP' END
                WHEN 4 THEN 'QNO' WHEN 5 THEN 'OSN' WHEN 6 THEN 'DER' /*WHEN 7 THEN 'ACT'*/
                /*WHEN 21 THEN 'OSW'*/ WHEN 22 THEN 'QUA' END) NOT IN
                (select REGEXP_SUBSTR(filename,'([a-zA-Z]{3,4})(\.(zip|tgz|tar))',1,1,'c',1) from porusr.validity_checks where order_detail_id = $imageid)
                union all
                select
                '-DATA-',
                'Not Delivered' from dual where not exists
                (select 1 from PORUSR.ORDER_DETAILS
                where ORDER_DETAIL_STATUS_ID = 11
                and ID_ORDER_DETAIL = $imageid);
                exit
EOF`

                ret=$?
                if [ $ret -eq 0 ]
                then
                        #Cleaning line break, because  "no row selected" contains an initial line break
                        output=$(echo $output |tr -d '\n')
                        #echo $output

                        log_message "Got the following output from CSN_SMON_20 query: $output" "$logfile"
                        if [ "$output" == "no rows selected" ]
                        then
                                log_message "CSN_SMON_20 set as OK for image $imageid, as no results were obtained from database query"
                                state=OK
                        else
                                log_message "CSN_SMON_20 set as CRITICAL for image $imageid, as results were obtained from database query"
                                state=CRITICAL
                        fi

                        #echo $state
                else
                        log_message "Error $ret while querying status for CSN_SMON_20" "$logfile"
                fi


                $SQLITE $DATABASE <<EOF
                update acquisitions set CSN_SMON_20="$state" where service_id="$imageid";
EOF

        ret=$?
        if [ $ret -eq 0 ]
        then
            log_message "Updated SQLITE DB: update acquisitions set CSN_SMON_20=$state where service_id=$imageid;" "$logfile"
        else
            log_message "Error $ret while updating state $state for CSN_SMON_20 column on image $imageid" "$logfile"
        fi

    else
        log_message "Skipping check of CSN_SMON_20 for image $imageid as is already on OK or N/A status"
    fi


}


update_CSN_SMON_80_status () {

# # # This function first checks the status of the CSN_SMON_18 table on the database and if it is on critical or empty, it tries to update the status on the sqlite database.
# # # Argument: Image ID

#The query will output the count of results. If the first result is 0, then the column will be set as N/A. If it's more than 0, a second query will be run

#Example output:
#4

imageid=$1

check_sensor_status "$imageid" "CSN_SMON_80"

CSN_SMON_80=$?



if [ "$CSN_SMON_80" == "1" ]
then
        sleep 1

        output=`sqlplus -s $DB_CREDENTIALS << EOF

        column OGC_ID format a25;
        set pagesize 0
        set trimspool on
        set headsep off
        set echo off
        set feedback off
        set heading off
        set verify off
        set linesize 200
        set term off
        set head off

        select count(*) from (
        select *
        from porusr.order_details,
                 porusr.sib_service_types,
                 xmltable(('"'||REPLACE(SERVICE_ELEMENT_ID_LIST_SO||','||SERVICE_ELEMENT_ID_LIST_SP,',','","')||'"')) elements,
                 porusr.sib_service_elements
        where order_details.SERVICE_TYPE_ID=sib_service_types.id_service_type
          and sib_service_elements.id_service_element=to_number(trim(elements.COLUMN_VALUE))
          and sib_service_elements.JOU_FINSYS_LABEL='OIL_SPILL_DETECTION'
          and order_details.id_order_detail = $imageid);
        exit
EOF`


        ret=$?
        if [ $ret -eq 0 ]
        then
                log_message "Got the following output from CSN_SMON_80 query: $output" "$logfile"

        else
                log_message "Error $ret while querying status for CSN_SMON_80" "$logfile"

        fi


        state="OK"
        log_message "Default value OK for CSN_SMON_80 for image $imageid" "$logfile"

        if [ "$output" -eq "0" ]
        then
                state="N/A"
                log_message "Setting CSN_SMON_80 as N/A for image $imageid" "$logfile"
        else
                log_message "First query result for CSN_SMON_80 for $imageid is: $output" "$logfile"
                if [ "$output" -gt "0" ]
                then
                        sleep 1

                        result=`sqlplus -s $DB_CREDENTIALS << EOF

                        column OGC_ID format a25;
                        set pagesize 0
                        set trimspool on
                        set headsep off
                        set echo off
                        set feedback off
                        set heading off
                        set verify off
                        set linesize 200
                        set term off
                        set head off

                        SELECT count(1)
                        FROM OASUSR.OAS_ALERTS,
                        OASUSR.OAS_CS_SCHEDULED_ALERTS
                        WHERE OGC_ID=$imageid and
                        ALERT_ID=ID_ALERT;
                        exit
EOF`

                        log_message "Second query result for CSN_SMON_80 for $imageid is: $output" "$logfile"
                        if [ "$result" -eq "0" ]
                        then
                                state="CRITICAL"
                                log_message "Setting CSN_SMON_80 as CRITICAL for image $imageid" "$logfile"
                        fi
                fi
        fi

        log_message "Updating CSN_SMON_80 as "$state" for image $imageid" "$logfile"
        #Update the database with the value
        $SQLITE $DATABASE <<EOF
        update acquisitions set CSN_SMON_80="$state" where service_id="$imageid";
EOF

else
        log_message "Skipping check of CSN_SMON_80 for image $imageid as is already on OK or N/A status"
fi


}


update_CSN_SMON_90_status () {

# # # This function first checks the status of the CSN_SMON_90 table on the database and if it is on critical or empty, it tries to update the status on the sqlite database.
# # # Argument: Image ID

#The query will output the count of alerts. If higher than 0 it will be set as CRITICAL.

#Example output:
#0

# CSN_SMON_90 - Alerts Report Sent to end-users;

#Check if the generated Alert Report was not sent (due to an error) to at least one recipient (This check is done by querying the database).

#Input: The unique identification of a service : Service ID;
#Output: The Nagios sensor should return the following status:
# •      N/A – If the service belongs to COPERNICUS, EFCA_ATLANTIC, EFCA_MEDITERRANEAN, MAOC-N, FRONTEX*, or IMS_*;
#•       OK – If the Alert Report was correctly sent to all recipients (status = 1);
#•       CRITITCAL – If the Alert Report was incorrectly send to at least one recipient (status <> 1);


imageid=$1

check_sensor_status "$imageid" "CSN_SMON_90"

CSN_SMON_90=$?




if [ "$CSN_SMON_90" == "1" ]
then



        log_message "CSN_SMON_90 needs and update for image $imageid, retrieving data" "$logfile"

        PROJECTS=`$SQLITE $DATABASE <<EOF
select projects from acquisitions where service_id="$imageid";
EOF`

        log_message "Image $imageid got the following projects on SQLITE database: $PROJECTS"

        state="CRITICAL"
        PROJECTS=`echo $PROJECTS | grep "CLEANSEANET" | awk -F "," '{print $1}'`

        if [ "$PROJECTS" != "CLEANSEANET" ]
        then
                state="N/A"
                log_message "CSN_SMON_90 set as N/A for image $imageid" "$logfile"

        else

                sleep 1
                output=`sqlplus -s $DB_CREDENTIALS << EOF

                        set pagesize 0
                        set trimspool on
                        set headsep off
                        set echo off
                        set feedback off
                        set heading off
                        set verify off
                        set linesize 200
                        set term off
                        set head off

                SELECT
                COUNT(*)
                FROM OASUSR.OAS_ALERTS, OASUSR.OAS_CS_SCHEDULED_ALERTS, OASUSR.OAS_SENT_ALERTS, OASUSR.OAS_ALERT_CHANNELS
                WHERE OAS_ALERTS.ID_ALERT = OAS_CS_SCHEDULED_ALERTS.ALERT_ID
                AND OAS_CS_SCHEDULED_ALERTS.ID_CS_SCHEDULED_ALERT = OAS_SENT_ALERTS.CS_SCHEDULED_ALERT_ID
                AND ID_ALERT_CHANNEL = ALERT_CHANNEL_ID
                AND STATUS <> 1
                AND ID_ALERT_CHANNEL = 2 /* EMAIL*/
                AND OGC_ID = $imageid;
                exit
EOF`


                ret=$?
                if [ $ret -eq 0 ]
                then
                        log_message "Got the following output from CSN_SMON_90 query: $output" "$logfile"

                else
                        log_message "Error $ret while querying status for CSN_SMON_90" "$logfile"

                fi

                value=`echo $output | awk '{print $1}'`


                if [ "$value" == "0" ]
                then
                        log_message "Setting CSN_SMON_90 as OK for image $imageid" "$logfile"
                        state="OK"
                else
                        log_message "Setting CSN_SMON_90 as CRITICAL for image $imageid" "$logfile"
                        state="CRITICAL"
                fi
        fi

                #Update the database with the value
                $SQLITE $DATABASE <<EOF
                update acquisitions set CSN_SMON_90="$state" where service_id="$imageid";
EOF
                ret=$?
                if [ $ret -eq 0 ]
                then
                        log_message "Updated SQLITE DB: update acquisitions set CSN_SMON_90=$state where service_id=$imageid;" "$logfile"
                else
                        log_message "Error $ret while updating state $state for CSN_SMON_90 column on image $imageid " "$logfile"
                fi


else
        log_message "Skipping check of CSN_SMON_90 for image $imageid as is already on OK or N/A status"
fi


}

update_CSN_SMON_100_status () {

    # # # This function first checks the status of the CSN_SMON_100 table on the database and if it is on critical or empty, it tries to update the status on the sqlite database.
    # # # Argument: Image ID

    imageid=$1

    check_sensor_status "$imageid" "CSN_SMON_100"

    CSN_SMON_100=$?

    if [ "$CSN_SMON_100" == "1" ]
    then
        log_message "CSN_SMON_100 needs an update for image $imageid, retrieving data" "$logfile"

        PROJECTS=`$SQLITE $DATABASE <<EOF
            select projects from acquisitions where service_id="$imageid";
EOF`



                if [[ "$PROJECTS" != *,* ]] && [[ "$PROJECTS" =~ (COPERNICUS|EFCA_ATLANTIC|EFCA_MEDITERRANEAN|MAOC-N|FRONTEX|IMS_) ]]
                then
                        state="N/A"
                        log_message "CSN_SMON_100 set as N/A for image $imageid, as it is a single project $PROJECTS that matches COPERNICUS|EFCA_ATLANTIC|EFCA_MEDITERRANEAN|MAOC-N|FRONTEX|IMS_" "$logfile"


                else
                output=`sqlplus -s $DB_CREDENTIALS << EOF


                    SELECT '-DATA-', (SELECT LASTNAME FROM PORUSR.SIB_USERS WHERE PORUSR.SIB_USERS.ID_USER = CS_USER_ID) AS COASTAL_STATE
                    FROM OASUSR.OAS_ALERTS, OASUSR.OAS_CS_SCHEDULED_ALERTS, OASUSR.OAS_SENT_ALERTS
                    WHERE OAS_ALERTS.ID_ALERT = OAS_CS_SCHEDULED_ALERTS.ALERT_ID
                    AND OAS_CS_SCHEDULED_ALERTS.ID_CS_SCHEDULED_ALERT = OAS_SENT_ALERTS.CS_SCHEDULED_ALERT_ID(+)
                    AND OGC_ID = $imageid
                    AND ALERT_CHANNEL_ID = 2
                    GROUP BY CS_USER_ID
                    HAVING COUNT(OAS_SENT_ALERTS.CS_SCHEDULED_ALERT_ID) = 0;

                exit
EOF`



                ret=$?
                if [ $ret -eq 0 ]
                then
                                        #Cleaning line break, because  "no row selected" contains an initial line break
                                        output=$(echo $output |tr -d '\n')
                                        #echo $output

                    log_message "Got the following output from CSN_SMON_100 query: $output" "$logfile"
                                        if [ "$output" == "no rows selected" ]
                                        then
                                                log_message "CSN_SMON_100 set as OK for image $imageid, as no results were obtained from database query"
                                                state=OK
                                        else
                                                log_message "CSN_SMON_100 set as CRITICAL for image $imageid, as results were obtained from database query"
                                                state=CRITICAL
                                        fi

                                        #echo $state


                else
                    log_message "Error $ret while querying status for CSN_SMON_100" "$logfile"

                fi


                fi

                $SQLITE $DATABASE <<EOF
                update acquisitions set CSN_SMON_100="$state" where service_id="$imageid";
EOF

        ret=$?
        if [ $ret -eq 0 ]
        then
            log_message "Updated SQLITE DB: update acquisitions set CSN_SMON_100=$state where service_id=$imageid;" "$logfile"
        else
            log_message "Error $ret while updating state $state for CSN_SMON_100 column on image $imageid" "$logfile"
        fi

    else
        log_message "Skipping check of CSN_SMON_100 for image $imageid as is already on OK or N/A status"
    fi

}




#Execute main functions:
echo "" >> "$logfile"
echo "-----------------------------------------------------------------------------------------------------" >> "$logfile"
log_message "Starting execution of main process" "$logfile"

database_creation

####The function call is disabled as the below function "get_current_acquisitions_provider" gives the same data, giving also the provier column
####get_current_acquisitions "$current_acquisitions_outfile"

get_current_acquisitions_provider "$current_acquisitions_provider_outfile"

#We set all the actives acquisitions to inactive, they will be set as active on the function insert_acquisitions_provider_data_into_database
set_acquisitions_to_inactive

insert_acquisitions_provider_data_into_database "$current_acquisitions_provider_outfile"


log_message "Main process execution ended" "$logfile"



echo "-----------------------------------------------------------------------------------------------------" >> "$logfile"


# update_CSN_SMON_10_status "2409060001"
# update_CSN_SMON_80_status "2409060015"
#update_CSN_SMON_80_status "2409050033"
#update_CSN_SMON_100_status  "2409110026" ""


#update_CSN_SMON_20_status "2409090013"

cp /usr/local/nagios/New-EODC-Monitoring/EODC_ACQUISITIONS.db /usr/local/nagios/share/dashboard/New-EODC-Acquisition/EODC_ACQUISITIONS.db

python /usr/local/nagios/New-EODC-Monitoring/jira_integration.py

cp /usr/local/nagios/New-EODC-Monitoring/EODC_ACQUISITIONS.db /usr/local/nagios/share/dashboard/New-EODC-Acquisition/EODC_ACQUISITIONS.db
script.sh
34 KB